using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WalletWasabi.Tor.Http.Helpers;
using WalletWasabi.Tor.Http.Models;

namespace WalletWasabi.Tor.Http.Extensions
{
	public static class HttpResponseMessageExtensions
	{
		public static async Task<HttpResponseMessage> CreateNewAsync(Stream responseStream, HttpMethod requestMethod)
		{
			// https://tools.ietf.org/html/rfc7230#section-3
			// The normal procedure for parsing an HTTP message is to read the
			// start - line into a structure, read each header field into a hash table
			// by field name until the empty line, and then use the parsed data to
			// determine if a message body is expected.If a message body has been
			// indicated, then it is read as a stream until an amount of octets
			// equal to the message body length is read or the connection is closed.

			// https://tools.ietf.org/html/rfc7230#section-3
			// All HTTP/ 1.1 messages consist of a start - line followed by a sequence
			// of octets in a format similar to the Internet Message Format
			// [RFC5322]: zero or more header fields(collectively referred to as
			// the "headers" or the "header section"), an empty line indicating the
			// end of the header section, and an optional message body.
			// HTTP - message = start - line
			//					* (header - field CRLF )
			//					CRLF
			//					[message - body]

			Debug.WriteLine("[client] About to read start line.");
			string startLine = await HttpMessageHelper.ReadStartLineAsync(responseStream).ConfigureAwait(false);
			Debug.WriteLine($"[client] startLine: '{startLine}'");

			var statusLine = StatusLine.Parse(startLine);
			var response = new HttpResponseMessage(statusLine.StatusCode);

			Debug.WriteLine("[client] About to read headers.");
			string headers = await HttpMessageHelper.ReadHeadersAsync(responseStream).ConfigureAwait(false);
			Debug.WriteLine("[client] headers: '{0}'", headers);

			var headerSection = await HeaderSection.CreateNewAsync(headers).ConfigureAwait(false);
			var headerStruct = headerSection.ToHttpResponseHeaders();

			HttpMessageHelper.AssertValidHeaders(headerStruct.ResponseHeaders, headerStruct.ContentHeaders);
			byte[] contentBytes = await HttpMessageHelper.GetContentBytesAsync(responseStream, headerStruct, requestMethod, statusLine).ConfigureAwait(false);
			contentBytes = HttpMessageHelper.HandleGzipCompression(headerStruct.ContentHeaders, contentBytes);
			response.Content = contentBytes is null ? null : new ByteArrayContent(contentBytes);

			HttpMessageHelper.CopyHeaders(headerStruct.ResponseHeaders, response.Headers);
			if (response.Content is { })
			{
				HttpMessageHelper.CopyHeaders(headerStruct.ContentHeaders, response.Content.Headers);
			}
			return response;
		}

		public static async Task ThrowRequestExceptionFromContentAsync(this HttpResponseMessage me)
		{
			var errorMessage = "";

			if (me.Content is { })
			{
				var contentString = await me.Content.ReadAsStringAsync().ConfigureAwait(false);

				// Remove " from beginning and end to ensure backwards compatibility and it's kindof trash, too.
				if (contentString.Count(f => f == '"') <= 2)
				{
					contentString = contentString.Trim('"');
				}

				if (!string.IsNullOrWhiteSpace(contentString))
				{
					errorMessage = $"\n{contentString}";
				}
			}

			throw new HttpRequestException($"{me.StatusCode.ToReasonString()}{errorMessage}");
		}
	}
}
