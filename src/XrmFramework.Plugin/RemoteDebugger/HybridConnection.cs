﻿
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace XrmFramework.RemoteDebugger
{
    public class HybridConnection : IDisposable
    {
        private readonly string _uri;
        private static readonly DateTime EpochTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        private readonly HttpClient _client;

        public HybridConnection(string keyName, string sharedAccessKey, string uri)
        {
            _uri = uri;
            var token = BuildSignature(keyName, Encoding.UTF8.GetBytes(sharedAccessKey), uri, TimeSpan.FromMinutes(5));

            _client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
            _client.DefaultRequestHeaders.Add("ServiceBusAuthorization", token);
        }

        public async Task<RemoteDebuggerMessage> SendMessage(RemoteDebuggerMessage message)
        {
            var serializedContext = JsonConvert.SerializeObject(message);

            var request = new HttpRequestMessage(HttpMethod.Post, _uri)
            {
                Content = new StringContent(serializedContext, Encoding.UTF8, "application/json")
            };

            var response = await _client.SendAsync(request);

            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {

                var responseMessage = JsonConvert.DeserializeObject<RemoteDebuggerMessage>(responseString);

                return responseMessage;
            }

            response.EnsureSuccessStatusCode();
            return null;
        }


        private static string BuildSignature(
            string keyName,
            byte[] encodedSharedAccessKey,
            string resource,
            TimeSpan timeToLive)
        {
            var str1 = BuildExpiresOn(timeToLive);
            var str2 = WebUtility.UrlEncode(resource);
            var str3 = Sign(string.Join("\n", new List<string>()
            {
                str2,
                str1
            }), encodedSharedAccessKey);
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}={2}&{3}={4}&{5}={6}&{7}={8}", "SharedAccessSignature", "sr", str2, "sig", WebUtility.UrlEncode(str3), "se", WebUtility.UrlEncode(str1), "skn", WebUtility.UrlEncode(keyName));
        }

        private static string Sign(string requestString, byte[] encodedSharedAccessKey)
        {
            using (var hmacshA256 = new HMACSHA256(encodedSharedAccessKey))
            {
                return Convert.ToBase64String(hmacshA256.ComputeHash(Encoding.UTF8.GetBytes(requestString)));
            }
        }

        private static string BuildExpiresOn(TimeSpan timeToLive)
        {
            return Convert.ToString(Convert.ToInt64(DateTime.UtcNow.Add(timeToLive).Subtract(EpochTime).TotalSeconds, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        #region IDisposable

        public void Dispose()
        {
            _client?.Dispose();
        }
        #endregion

        public static bool TryPingDebugSession(DebugSession debugSession)
        {
            var uri = new Uri($"{debugSession.RelayUrl}/{debugSession.HybridConnectionName}");

            var isOnline = false;

            try
            {
                using var hybridConnection = new HybridConnection(debugSession.SasKeyName, debugSession.SasConnectionKey, uri.AbsoluteUri);

                var message = new RemoteDebuggerMessage(RemoteDebuggerMessageType.Ping, null, Guid.NewGuid());

                var response = hybridConnection.SendMessage(message).GetAwaiter().GetResult();

                isOnline = response.MessageType == RemoteDebuggerMessageType.Ping;
            }
            catch (HttpRequestException)
            {
                //isOnline is false and will stay that way
            }
            return isOnline;

        }
    }
}
