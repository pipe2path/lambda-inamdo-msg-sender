using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Net.Http;
using Sinch.ServerSdk;

using Amazon.Lambda.Core;
using Newtonsoft.Json;
using System.Text;
using System.Threading;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace inamdo_msg_sender
{
    public class Function
    {

        private static Timer timer;
        private static readonly HttpClient client = new HttpClient();
        
        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool FunctionHandler(ILambdaContext context)
        {
            bool success = false;

            try
            {
                //return input?.ToUpper();
                var userTask = getUsers();

                userTask.ContinueWith(task =>
                {
                    //var users = task.Result;
                    //foreach (User u in users)
                    //Console.WriteLine(u.userName.ToString());
                    Console.WriteLine("Processing done ...");
                },
                TaskContinuationOptions.OnlyOnRanToCompletion);
                Console.ReadLine();

                success = true;
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return success; 
        }

        static async Task<IEnumerable<User>> getUsers()
        {
            string getPath = "http://localhost:13985/api/users/couponlist";
            string putPath = "http://localhost:13985/api/messages";
            string getMsgPath = "http://localhost:13985/api/messages/user";
            ObjectId userId = new ObjectId();
            string phoneNum = "";
            string smsMessage = "";
            string code = "";

            //await Task.Delay(3000);

            var response = await client.GetAsync(getPath);
            response.EnsureSuccessStatusCode();
            var stringResult = await response.Content.ReadAsStringAsync();
            IEnumerable<User> users = JsonConvert.DeserializeObject<IEnumerable<User>>(stringResult);

            // process messages
            foreach (User u in users)
            {
                userId = u.userId;
                phoneNum = u.userPhone;
                code = u.code.ToString();

                // check if user has been sent a message in the past 15 days
                double daysSinceLastMsg = await messageLastSent(userId, getMsgPath);
                if (daysSinceLastMsg > 15)
                {
                    smsMessage = u.message + " Please use code: " + code + " when you order.";
                    var smsApi = SinchFactory.CreateApiFactory("86be6998-e82f-49eb-9d8d-cdd2427ad4a9", "5MnvbXXhe0iMuzXjl02WWQ==").CreateSmsApi();
                    var sendSmsResponse = await smsApi.Sms("+19094524127", smsMessage).Send();
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    var smsMessageStatusResponse = await smsApi.GetSmsStatus(sendSmsResponse.MessageId);

                    if (smsMessageStatusResponse.Status == "Successful")
                    {
                        // update db
                        MessageJsonPayload payload = new MessageJsonPayload();
                        payload.userId = u.userId.ToString();
                        payload.userName = u.userName;
                        payload.userPhone = u.userPhone;
                        payload.userEmail = u.userEmail;
                        payload.message = u.message;
                        payload.code = u.code;

                        try
                        {
                            response = await client.PutAsync(putPath, new StringContent(payload.ToJson(), Encoding.UTF8, "application/json"));
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                }
            }
            return users;
        }

        static async Task<double> messageLastSent(ObjectId userId, string getMsgPath)
        {
            var response = await client.GetAsync(getMsgPath);
            response.EnsureSuccessStatusCode();
            var stringResult = await response.Content.ReadAsStringAsync();
            List<Message> messages = JsonConvert.DeserializeObject<List<Message>>(stringResult);
            DateTime msgLastSent = DateTime.Today;
            double numOfDays = 0;

            if (messages != null && messages.Count > 0)
            {
                foreach (Message msg in messages)
                {
                    msgLastSent = msg.dateLastTextSent;
                    break;
                }
            }

            TimeSpan ts = DateTime.Today.Subtract(msgLastSent);
            numOfDays = ts.TotalDays;
            return numOfDays;

        }
    }

    public class User
    {

        // not using userId because NewtonSoft cannot deserialize an ObjectId type. Throws an error. Will use code and userPhone for uniqueness.

        [JsonConverter(typeof(ObjectIdConverter))]
        public ObjectId userId { get; set; }

        //[BsonId]
        //[BsonRepresentation(BsonType.ObjectId)]
        //public string internalId { get; set; }

        public int code { get; set; }
        public string userName { get; set; }
        public string userPhone { get; set; }
        public string userEmail { get; set; }
        public string message { get; set; }
        public bool optIn { get; set; }
    }

    public class Message
    {
        [BsonId]
        public ObjectId internalId { get; set; }

        public string userId { get; set; }
        public string userName { get; set; }
        public string userPhone { get; set; }
        public string userEmail { get; set; }
        public string message { get; set; }
        public int code { get; set; }
        public DateTime dateLastTextSent { get; set; }
    }

    public class MessageJsonPayload
    {
        public string userId { get; set; }
        public string userName { get; set; }
        public string userPhone { get; set; }
        public string userEmail { get; set; }
        public int code { get; set; }
        public string message { get; set; }
    }

    public class ObjectIdConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ObjectId);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String)
                throw new Exception($"Unexpected token parsing ObjectId. Expected String, got {reader.TokenType}.");

            var value = (string)reader.Value;
            return string.IsNullOrEmpty(value) ? ObjectId.Empty : new ObjectId(value);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is ObjectId)
            {
                var objectId = (ObjectId)value;
                writer.WriteValue(objectId != ObjectId.Empty ? objectId.ToString() : string.Empty);
            }
            else
            {
                throw new Exception("Expected ObjectId value.");
            }
        }
    }
}
