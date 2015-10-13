using NLog;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Teltec.Backup.Ipc.TcpSocket
{
	public class ClientEventArgs : PeerEventArgs
	{
	}

	public class ClientConnectedEventArgs : ClientEventArgs
	{
	}

	public class ClientReceiveEventArgs : ClientEventArgs
	{
		public int BytesReceived;
		public byte[] Data;
	}

	public class ClientSendEventArgs : ClientEventArgs
	{
		public int BytesSent;
	}

	public class ClientErrorEventArgs : ClientEventArgs
	{
		public string Reason;
		public Exception Exception;
	}

	public delegate void ClientConnectEventHandler(object sender, ClientConnectedEventArgs e);
	public delegate void ClientReceiveEventHandler(object sender, ClientReceiveEventArgs e);
	public delegate void ClientSendEventHandler(object sender, ClientSendEventArgs e);
	public delegate void ClientErrorEventHandler(object sender, ClientErrorEventArgs e);

	public class Client : Shared, IDisposable
	{
		public class LocalContext
		{
			public static readonly int BufferSize = 4096; // 4 KB
			public byte[] RecvBuffer = new byte[BufferSize];
			//public ConcurrentQueue<byte[]> InBuffer = new ConcurrentQueue<byte[]>();
			public ConcurrentQueue<byte[]> OutBuffer = new ConcurrentQueue<byte[]>();

			public Socket ClientSocket;

			public volatile bool ShouldStopWorkerThread = false;
			public Thread WorkerThread = null;

			public void Reset()
			{
				if (ClientSocket != null)
				{
					ClientSocket = null;
				}
				if (WorkerThread != null)
				{
					WorkerThread = null;
				}
			}
		}

		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		private LocalContext Context = new LocalContext();

		private AutoResetEvent ClientFinished = new AutoResetEvent(false);

		private void Reset()
		{
			Context.Reset();
			Context.ShouldStopWorkerThread = false;
		}

		public Client(ISynchronizeInvoke owner) : base(owner)
		{
			CreateWorkerThread();
		}

		~Client()
		{
			Dispose(false);
		}

		#region Worker thread

		private void DestroyWorkerThread()
		{
			if (Context.WorkerThread == null)
				return;

			Context.ShouldStopWorkerThread = true;
			Context.WorkerThread.Join();

			Context.WorkerThread = null;
		}

		private void CreateWorkerThread()
		{
			Context.WorkerThread = new Thread(ProcessSocketStatic);
			Context.WorkerThread.IsBackground = true;
			Context.WorkerThread.Start(new ThreadArgument
			{
				Client = this,
				ClientContext = Context,
			});
		}

		private class ThreadArgument
		{
			public Client Client;
			public LocalContext ClientContext;

			public delegate void ActionDelegate(LocalContext context);
			public ActionDelegate StartCallback;
			public ActionDelegate FinishCallback;
		}

		private static void ProcessSocketStatic(object param)
		{
			ThreadArgument obj = (ThreadArgument)param;
			obj.Client.ProcessSocket(obj);
		}

		private void ProcessSocket(ThreadArgument param)
		{
			LocalContext context = param.ClientContext;

			Thread.CurrentThread.Name = "worker_proc";

			// Start up.
			if (param.StartCallback != null)
				param.StartCallback(context);

			try
			{
				int pollTimeout = 1000; // In microseconds

				while (!context.ShouldStopWorkerThread)
				{
					if (context == null || context.ClientSocket == null)
					{
						ConnectedEvent.WaitOne(); // Wait for reconnection.
					}

					try
					{
						bool canRead = context.ClientSocket.Poll(pollTimeout, SelectMode.SelectRead);
						// true if:
						// - The Listen method has been called and a connection is pending.
						// - Data is available for reading.
						// - The connection has been closed, reset, or terminated.
						if (canRead)
						{
							// Read data and enqueue on the read queue.
							try
							{
								byte[] buffer = context.RecvBuffer;
								context.ClientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None,
									new AsyncCallback(EndReceive), context);
							}
							catch (SocketException ex)
							{
								logger.Error("ERROR BeginReceive: {0}", ex.Message);
								HandleSocketError(ex.SocketErrorCode, context);
							}
						}

						// Check whether the previous read indicated a client disconnection.
						if (context.ClientSocket == null)
							break;

						bool canWrite = context.ClientSocket.Poll(pollTimeout, SelectMode.SelectWrite);
						// true if:
						// - A Connect method call has been processed, and the connection has succeeded.
						// - Data can be sent.
						if (canWrite)
						{
							// Dequeue data from write queue and send it.
							if (!context.OutBuffer.IsEmpty)
							{
								byte[] buffer = null;
								context.OutBuffer.TryDequeue(out buffer);

								try
								{
									context.ClientSocket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None,
										new AsyncCallback(EndSend), context);
								}
								catch (SocketException ex)
								{
									logger.Error("ERROR BeginSend: {0}", ex.Message);
									HandleSocketError(ex.SocketErrorCode, context);
								}
							}
						}
					}
					catch (ObjectDisposedException)
					{
						// Socket was already closed.
						ConnectedEvent.WaitOne(); // Wait for reconnection.
					}

					Thread.Sleep(1);
				}
			}
			catch (ThreadInterruptedException)
			{
				// Handle interruption.
				logger.Debug("Interrupted thread {0}", Thread.CurrentThread.Name);
			}
			finally
			{
				logger.Debug("Cleaning up thread {0}", Thread.CurrentThread.Name);

				// Clean up.
				if (param.FinishCallback != null)
					param.FinishCallback(context);
			}
		}

		#endregion

		#region Events

		public AutoResetEvent ConnectedEvent = new AutoResetEvent(false);
		public event ClientConnectEventHandler Connected;

		public AutoResetEvent DisconnectedEvent = new AutoResetEvent(false);
		public event ClientConnectEventHandler Disconnected;

		public AutoResetEvent MessageReceivedEvent = new AutoResetEvent(false);
		public event ClientReceiveEventHandler MessageReceived;

		public AutoResetEvent MessageSentEvent = new AutoResetEvent(false);
		public event ClientSendEventHandler MessageSent;

		public AutoResetEvent ConnectionFailedEvent = new AutoResetEvent(false);
		public event ClientErrorEventHandler ConnectionFailed;

		// This event is raised both when a disconnection or a connection error occurs.
		public AutoResetEvent NeedsReconnectionEvent = new AutoResetEvent(false);

		#endregion

		#region Properties

		private bool _IsRunning = false;
		public bool IsRunning
		{
			get { return _IsRunning; }
			private set { _IsRunning = value; }
		}

		public bool IsConnected
		{
			get { return IsSocketConnected(Context.ClientSocket); }
		}

		private string _RemoteIP;
		public string RemoteIP
		{
			get { return _RemoteIP; }
			internal set { _RemoteIP = value; }
		}

		private int _RemotePort;
		public int RemotePort
		{
			get { return _RemotePort; }
			internal set
			{
				if (value < 1 || value > short.MaxValue)
					throw new ArgumentException("Invalid port number");
				_RemotePort = value;
			}
		}

		#endregion

		#region Socket options

		public bool KeepAlive
		{
			get { return GetKeepAlive(Context.ClientSocket); }
		}

		#endregion

		#region Socket operations

		public void Connect(string ipAddress = "127.0.0.1", int port = 9911, bool enableKeepAlive = false)
		{
			Reset();

			IsRunning = true;

			RemoteIP = ipAddress;
			RemotePort = port;

			Context.ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			Context.ClientSocket.NoDelay = true;

			if (enableKeepAlive)
				SetKeepAlive(Context.ClientSocket, enableKeepAlive);

			IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(RemoteIP), RemotePort);

			logger.Debug("CONNECTING TO {0}:{1}", RemoteIP, RemotePort);
			Context.ClientSocket.BeginConnect(endpoint, new AsyncCallback(EndConnect), Context);
		}

		//public int Receive(out byte[] data)
		//{
		//	if (!Context.InBuffer.IsEmpty)
		//	{
		//		bool removed = Context.InBuffer.TryDequeue(out data);
		//		return removed ? data.Length : 0;
		//	}
		//	else
		//	{
		//		data = null;
		//		return 0;
		//	}
		//}

		public void Send(byte[] data)
		{
			if (Context == null || Context.ClientSocket == null)
				return;

			int count = data.Length;
			byte[] copiedData = new byte[count];
			Buffer.BlockCopy(data, 0, copiedData, 0, count);

			Context.OutBuffer.Enqueue(copiedData);
		}

		public void Disconnect()
		{
			if (Context.ClientSocket == null)
				return;

			try
			{
				Context.ClientSocket.BeginDisconnect(false, new AsyncCallback(EndDisconnect), Context);
			}
			catch (SocketException ex)
			{
				logger.Error("ERROR BeginDisconnect: {0}", ex.Message);
				HandleSocketError(ex.SocketErrorCode, Context);
			}
		}

		#endregion

		#region Socket callbacks

		private void EndConnect(IAsyncResult iar)
		{
			if (iar == null || iar.AsyncWaitHandle == null)
				return;

			LocalContext context = (LocalContext)iar.AsyncState;
			try
			{
				Context.ClientSocket.EndConnect(iar);
				//logger.Debug("CONNECTED TO {0}", Context.ClientSocket.RemoteEndPoint.ToString());

				OnConnected(new ClientConnectedEventArgs { });

				ConnectedEvent.Set();
			}
			catch (ObjectDisposedException)
			{
				// Socket was already closed.
			}
			catch (SocketException ex)
			{
				logger.Error("ERROR EndConnect: {0}", ex.Message);
				HandleSocketError(ex.SocketErrorCode, context);

				OnConnectionFailed(new ClientErrorEventArgs { Reason = ex.Message, Exception = ex });

				ConnectionFailedEvent.Set();
				NeedsReconnectionEvent.Set();
			}
		}

		private void EndDisconnect(IAsyncResult iar)
		{
			if (iar == null || iar.AsyncState == null)
				return;

			LocalContext context = (LocalContext)iar.AsyncState;
			try
			{
				if (context.ClientSocket != null)
					context.ClientSocket.EndDisconnect(iar);

				AfterEndDisconnect(context);
			}
			catch (ObjectDisposedException)
			{
				// Socket was already closed.
			}
			catch (SocketException ex)
			{
				logger.Error("ERROR DISCONNECTING: {0}", ex.Message);
				HandleSocketError(ex.SocketErrorCode, context);
			}
		}

		protected override void AfterEndDisconnect(object param)
		{
			LocalContext context = (LocalContext)param;

			CloseSocket(context);

			//logger.Debug("DISCONNECTED");

			OnDisconnected(new ClientConnectedEventArgs { });

			DisconnectedEvent.Set();
			NeedsReconnectionEvent.Set();

			//context.Dispose();
		}

		private void EndSend(IAsyncResult iar)
		{
			if (iar == null || iar.AsyncState == null)
				return;

			LocalContext context = (LocalContext)iar.AsyncState;
			try
			{
				int count = context.ClientSocket.EndSend(iar);

				Statistics.TotalBytesSent += count;

				OnMessageSent(new ClientSendEventArgs { BytesSent = count });

				MessageSentEvent.Set();
			}
			catch (ObjectDisposedException)
			{
				// Socket was already closed.
			}
			catch (SocketException ex)
			{
				logger.Error("ERROR SENDING: {0}", ex.Message);
				HandleSocketError(ex.SocketErrorCode, context);
			}
		}

		private void EndReceive(IAsyncResult iar)
		{
			if (iar == null || iar.AsyncState == null)
				return;

			LocalContext context = (LocalContext)iar.AsyncState;
			try
			{
				int count = context.ClientSocket.EndReceive(iar);

				if (count > 0)
				{
					// Copy `count` bytes from `RecvBuffer` to `copiedBuffer`.
					byte[] copiedBuffer = new byte[count];
					Buffer.BlockCopy(context.RecvBuffer, 0, copiedBuffer, 0, count);

					Statistics.TotalBytesReceived += count;

					OnMessageReceived(new ClientReceiveEventArgs
					{
						BytesReceived = count,
						Data = copiedBuffer
					});

					MessageReceivedEvent.Set();
				}
				else
				{
					AfterEndDisconnect(context);
				}
			}
			catch (ObjectDisposedException)
			{
				// Socket was already closed.
			}
			catch (SocketException ex)
			{
				logger.Error("ERROR RECEIVING: {0}", ex.Message);
				HandleSocketError(ex.SocketErrorCode, context);
			}
		}

		#endregion

		#region Delegate invokers

		protected virtual void OnConnected(ClientConnectedEventArgs e)
		{
			logger.Debug("CONNECTED TO {0}", RemoteIP);
			InvokeDelegate(Connected, e);
		}

		protected virtual void OnDisconnected(ClientConnectedEventArgs e)
		{
			logger.Debug("DISCONNECTED");
			InvokeDelegate(Disconnected, e);
		}

		protected virtual void OnMessageReceived(ClientReceiveEventArgs e)
		{
			logger.Debug("RECEIVED: {0}", Encoding.ASCII.GetString(e.Data, 0, e.Data.Length));
			InvokeDelegate(MessageReceived, e);
		}

		protected virtual void OnMessageSent(ClientSendEventArgs e)
		{
			InvokeDelegate(MessageSent, e);
		}

		protected virtual void OnConnectionFailed(ClientErrorEventArgs e)
		{
			InvokeDelegate(ConnectionFailed, e);
		}

		#endregion

		private void CloseSocket(LocalContext context)
		{
			if (context.ClientSocket != null)
			{
				context.ClientSocket.Shutdown(SocketShutdown.Both);
				context.ClientSocket.Close();
				//context.ClientSocket = null;
			}
		}

		#region Dispose Pattern Implementation

		bool _shouldDispose = true;
		bool _isDisposed;

		/// <summary>
		/// Implements the Dispose pattern
		/// </summary>
		/// <param name="disposing">Whether this object is being disposed via a call to Dispose
		/// or garbage collected.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!this._isDisposed)
			{
				if (disposing && _shouldDispose)
				{
					DestroyWorkerThread();
					CloseSocket(Context);
				}
				this._isDisposed = true;
			}
		}

		/// <summary>
		/// Disposes of all managed and unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion
	}
}