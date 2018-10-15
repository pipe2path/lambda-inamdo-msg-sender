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
        
        public bool FunctionHandler(ILambdaContext context)
        {
            bool success = false;

            try
            {
                //Write Log to Cloud Watch using Console.WriteLline.    
                Console.WriteLine("Execution started for function -  {0} at {1}",
                                    context.FunctionName, DateTime.Now);

                processUsers();

                success = true;

                //Write Log to cloud watch using context.Logger.Log Method  
                context.Logger.Log(string.Format("Finished execution for function -- {0} at {1}",
                                   context.FunctionName, DateTime.Now));

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during execution for function -- {0} at {1}", context.FunctionName, DateTime.Now);
                return false;
            }
        }

        static bool processUsers()
        {
            bool success = false;
            
            string getPath = "http://review.inamdo.com/api/users/couponlist";
            string putPath = "http://review.inamdo.com/api/messages";
            string getMsgPath = "http://review.inamdo.com/api/messages/user";
            ObjectId userId = new ObjectId();
            string phoneNum = "";
            string smsMessage = "";
            string code = "";
            string msgUserPath = "";

            var response = client.GetAsync(getPath).Result;
            string stringResult = response.Content.ReadAsStringAsync().Result;
            IEnumerable<User> users = JsonConvert.DeserializeObject<IEnumerable<User>>(stringResult);

            foreach (User u in users)
            {
                // send only if the message exists - after the owner submits a valid message
                if (u.message != null)
                {
                    userId = u.userId;
                    phoneNum = u.userPhone;
                    code = u.code.ToString();
                    
                    // check if user has been sent a message in the past 15 days
                    msgUserPath = getMsgPath + "?id=" + userId;
                    double daysSinceLastMsg = messageLastSent(msgUserPath);
                    //daysSinceLastMsg.ContinueWith(task => { }, TaskContinuationOptions.OnlyOnRanToCompletion);

                    if (daysSinceLastMsg == 0 || daysSinceLastMsg > 15)
                    {
                        smsMessage = u.message + " Please use code: " + code + " when you order.";
                        var smsStatus = SendSMS(phoneNum, smsMessage).GetAwaiter().GetResult();

                        if (smsStatus == "Successful")
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
                                response = client.PutAsync(putPath, new StringContent(payload.ToJson(), Encoding.UTF8, "application/json")).Result;
                            }
                            catch (Exception ex)
                            {
                                success = false;
                            }
                        }
                    }
                }
            }
            return success;
        }

        private static async Task<string> SendSMS(string phoneNum, string smsMessage)
        {
            var smsApi = SinchFactory.CreateApiFactory("86be6998-e82f-49eb-9d8d-cdd2427ad4a9", "5MnvbXXhe0iMuzXjl02WWQ==").CreateSmsApi();
            var sendSmsResponse = await smsApi.Sms("+1" + phoneNum, smsMessage).Send();
            await Task.Delay(TimeSpan.FromSeconds(10));
            var smsMessageStatusResponse = await smsApi.GetSmsStatus(sendSmsResponse.MessageId);
            return smsMessageStatusResponse.Status;
        }
        
        static double messageLastSent(string getMsgPath)
        {
            double numOfDays = 0;
            var response = client.GetAsync(getMsgPath).Result;
            if (response.IsSuccessStatusCode)
            {
                var stringResult = response.Content.ReadAsStringAsync().Result;
                List<Message> messages = JsonConvert.DeserializeObject<List<Message>>(stringResult);
                DateTime msgLastSent = DateTime.Today;
        
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
            }        
            return numOfDays;
        }
    }

    public class User
    {
        // not using userId because NewtonSoft cannot deserialize an ObjectId type. Throws an error. Will use code and userPhone for uniqueness.

        [JsonConverter(typeof(ObjectIdConverter))]
        public ObjectId userId { get; set; }

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
