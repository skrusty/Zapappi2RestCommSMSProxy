using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Web;

namespace Zapappi2RestCommProxy
{
    public static class Zapappi2RestCommProxy
    {
        [FunctionName("Zapappi2RestCommProxy")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                var restCommAccName = req.Query["restCommAccName"];
                var restCommUsername = req.Query["restCommUsername"];
                var restCommPassword = req.Query["restCommPassword"];
                var restCommUrl = $"https://{restCommAccName}.restcomm.com/restcomm/2012-04-24/Accounts/{restCommUsername}/SMS/Messages";

                // Parse out SMS
                string requestBodyString = await new StreamReader(req.Body).ReadToEndAsync();

                log.LogInformation($"New SMS received. {requestBodyString}");

                NewSMSWebhookMessage data = JsonConvert.DeserializeObject<NewSMSWebhookMessage>(requestBodyString);

                // Create RestComm Payload
                var rCommPayload = new RestCommPayload()
                {
                    From = data.Source,
                    To = data.Destination,
                    Body = data.Body
                };

                // Create new WebRequest
                var uri = new Uri(restCommUrl);
                var outReq = WebRequest.Create(uri);
                outReq.Method = "POST";

                // Add Authentication header
                string encoded = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes($"{restCommUsername}:{restCommPassword}"));
                outReq.Headers.Add("Authorization", "Basic " + encoded);

                // Create post data
                var postData = $"From={HttpUtility.UrlEncode(data.Source)}&";
                postData += $"To={HttpUtility.UrlEncode(data.Destination)}&";
                postData += $"Body={HttpUtility.UrlEncode(data.Body)}";
                var postEncoded = Encoding.ASCII.GetBytes(postData);

                // Add content type and length
                outReq.ContentType = "application/x-www-form-urlencoded";
                outReq.ContentLength = postEncoded.Length;

                // Add encoded content to stream
                using (var stream = outReq.GetRequestStream())
                {
                    stream.Write(postEncoded, 0, postEncoded.Length);
                }

                // Send the request and get a response
                var resp = outReq.GetResponse();

                // Read out the response from RestComm
                var restCommResponse = string.Empty;
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    restCommResponse = sr.ReadToEnd();
                }
                resp.Close();
                resp.Dispose();

                log.LogInformation($"RestComm Response: {restCommResponse}");

                // If we got here, then we're all good.
                return new OkResult();
            }catch (WebException wex)
            {
                // Something went wrong, lets return an error.
                log.LogError(wex, $"Failed to proxy message. {wex.Message}");
                return new StatusCodeResult(500);
            }
        }
    }

    public class NewSMSWebhookMessage
    {
        public Guid MessageId { get; set; }
        public string Source { get; set; }
        public string Destination { get; set; }
        public DateTime Received { get; set; }
        public string Body { get; set; }
        public Guid SubscriptionId { get; set; }
        public int CustomerId { get; set; }
    }

    public class RestCommPayload
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Body { get; set; }
        public string StatusCallback { get; set; }
    }
}
