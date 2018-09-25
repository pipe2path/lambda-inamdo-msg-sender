using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;

using inamdo_msg_sender;

namespace inamdo_msg_sender.Tests
{
    public class FunctionTest
    {
        [Fact]
        public void TestMessageSenderFunction()
        {

            // Invoke the lambda function and confirm the string was upper cased.
            var function = new Function();
            var context = new TestLambdaContext();
            bool success = function.FunctionHandler(context);

            Assert.True(success);
        }
    }
}
