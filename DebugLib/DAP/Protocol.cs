using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using PicoJson;
using PicoJson.Untyped;

namespace Maestro.Debug
{
	internal readonly struct Request
	{
		public readonly int seq;
		public readonly string command;

		internal Request(int seq, string command)
		{
			this.seq = seq;
			this.command = command;
		}
	}

	internal sealed class ProtocolServer
	{
		public delegate void OnRequest(Request request, JsonValue arguments);

		private const int BUFFER_SIZE = 4096;
		private const string TWO_CRLF = "\r\n\r\n";

		private static readonly Regex CONTENT_LENGTH_MATCHER = new Regex(@"Content-Length: (\d+)");
		private static readonly Encoding Encoding = System.Text.Encoding.UTF8;

		private readonly OnRequest onRequest;

		private int nextSequenceNumber = 1;
		private bool isServing = false;

		private Stream outputStream;
		private ByteBuffer rawData = new ByteBuffer();
		private int bodyLength = -1;

		public static JsonValue Response(JsonValue body = default)
		{
			return new JsonObject
			{
				{"success", true},
				{"body", body}
			};
		}

		public static JsonValue ErrorResponse(string errorMessage, JsonValue body = default)
		{
			return new JsonObject
			{
				{"success", false},
				{"message", errorMessage},
				{"body", body}
			};
		}

		public ProtocolServer(OnRequest onRequest)
		{
			this.onRequest = onRequest;
		}

		public void Start(IPAddress address, ushort port)
		{
			var serverSocket = new TcpListener(address, port);
			serverSocket.Start();

			var serverThread = new Thread(() => {
				while (true)
				{
					var clientSocket = serverSocket.AcceptSocket();
					if (clientSocket == null)
						continue;

					var clientThread = new Thread(() => {
						using (var stream = new NetworkStream(clientSocket))
						{
							try
							{
								Start(stream, stream);
							}
							catch { }
						}
						clientSocket.Close();
					});
					clientThread.IsBackground = true;
					clientThread.Start();
				}
			});
			serverThread.IsBackground = true;
			serverThread.Start();
		}

		public void Start(Stream inputStream, Stream outputStream)
		{
			this.outputStream = outputStream;

			var buffer = new byte[BUFFER_SIZE];

			isServing = true;
			while (isServing)
			{
				var read = inputStream.Read(buffer, 0, buffer.Length);
				if (read == 0)
					break;

				if (read > 0)
				{
					rawData.Append(buffer, read);
					ProcessData();
				}
			}
		}

		public void Stop()
		{
			isServing = false;
		}

		public void SendResponse(Request request, JsonValue body = default)
		{
			SendMessage(new JsonObject
			{
				{"type", "response"},
				{"request_seq", request.seq},
				{"command", request.command},
				{"success", true},
				{"body", body},
			});
		}

		public void SendErrorResponse(Request request, string errorMessage, JsonValue body = default)
		{
			SendMessage(new JsonObject
			{
				{"type", "response"},
				{"request_seq", request.seq},
				{"command", request.command},
				{"success", false},
				{"message", errorMessage},
				{"body", body},
			});
		}

		public void SendEvent(string eventName, JsonValue body = default)
		{
			SendMessage(new JsonObject {
				{"type", "event"},
				{"event", eventName},
				{"body", body}
			});
		}

		private void ProcessData()
		{
			while (true)
			{
				if (bodyLength >= 0)
				{
					if (rawData.Length >= bodyLength)
					{
						var buf = rawData.RemoveFirst(bodyLength);
						bodyLength = -1;
						Dispatch(Encoding.GetString(buf));
						continue;
					}
				}
				else
				{
					var s = rawData.GetString(Encoding);
					var idx = s.IndexOf(TWO_CRLF);
					if (idx != -1)
					{
						var m = CONTENT_LENGTH_MATCHER.Match(s);
						if (m.Success && m.Groups.Count == 2)
						{
							bodyLength = System.Convert.ToInt32(m.Groups[1].ToString());
							rawData.RemoveFirst(idx + TWO_CRLF.Length);
							continue;
						}
					}
				}
				break;
			}
		}

		private void Dispatch(string req)
		{
			if (!Json.TryDeserialize(req, out var message))
				return;

			switch (message["type"].wrapped)
			{
			case "request":
				{
					var request_seq = message["seq"].GetOr(0);
					var command = message["command"].GetOr("");
					var arguments = message["arguments"];

					var request = new Request(request_seq, command);

					try
					{
						onRequest(request, arguments);
					}
					catch (System.Exception e)
					{
						SendErrorResponse(request, $"error while processing request '{command}' (exception: {e.Message})");
					}
					break;
				}
			}
		}

		private void SendMessage(JsonValue message)
		{
			message["seq"] = nextSequenceNumber++;

			try
			{
				var data = ConvertToBytes(message);
				outputStream.Write(data, 0, data.Length);
				outputStream.Flush();
			}
			catch { }
		}

		private static byte[] ConvertToBytes(JsonValue message)
		{
			var asJson = Json.Serialize(message);
			var jsonBytes = Encoding.GetBytes(asJson);

			var header = string.Format("Content-Length: {0}{1}", jsonBytes.Length, TWO_CRLF);
			var headerBytes = Encoding.GetBytes(header);

			var data = new byte[headerBytes.Length + jsonBytes.Length];
			System.Buffer.BlockCopy(headerBytes, 0, data, 0, headerBytes.Length);
			System.Buffer.BlockCopy(jsonBytes, 0, data, headerBytes.Length, jsonBytes.Length);

			return data;
		}
	}

	internal sealed class ByteBuffer
	{
		private byte[] buffer = new byte[0];

		public int Length
		{
			get { return buffer.Length; }
		}

		public string GetString(Encoding enc)
		{
			return enc.GetString(buffer);
		}

		public void Append(byte[] b, int length)
		{
			var newBuffer = new byte[buffer.Length + length];
			System.Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
			System.Buffer.BlockCopy(b, 0, newBuffer, buffer.Length, length);
			buffer = newBuffer;
		}

		public byte[] RemoveFirst(int n)
		{
			var b = new byte[n];
			System.Buffer.BlockCopy(buffer, 0, b, 0, n);
			var newBuffer = new byte[buffer.Length - n];
			System.Buffer.BlockCopy(buffer, n, newBuffer, 0, buffer.Length - n);
			buffer = newBuffer;
			return b;
		}
	}
}
