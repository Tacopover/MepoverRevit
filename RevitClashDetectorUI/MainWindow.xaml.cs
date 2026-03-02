using System.IO;
using System.IO.Pipes;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RevitClashDetectorUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private NamedPipeClientStream pipeClient;
        private StreamReader reader;
        private StreamWriter writer;
        private DispatcherTimer connectionTimer;

        public MainWindow()
        {
            InitializeComponent();
            StartClient();
        }

        private void StartClient()
        {
            connectionTimer = new DispatcherTimer();
            connectionTimer.Interval = TimeSpan.FromSeconds(1); // Check every second
            connectionTimer.Tick += ConnectionTimer_Tick;
            connectionTimer.Start();
        }

        private async void ConnectionTimer_Tick(object sender, EventArgs e)
        {
            if (pipeClient == null || !pipeClient.IsConnected)
            {
                try
                {
                    pipeClient = new NamedPipeClientStream(".", "RevitChat", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipeClient.ConnectAsync(1000); // 1 second timeout

                    if (pipeClient.IsConnected)
                    {
                        connectionTimer.Stop();
                        MessagesTextBox.AppendText("Connected to server.\n");
                        reader = new StreamReader(pipeClient);
                        writer = new StreamWriter(pipeClient) { AutoFlush = true };
                        ListenForMessages();
                    }
                }
                catch (TimeoutException)
                {
                    MessagesTextBox.AppendText("Connection attempt timed out. Retrying...\n");
                }
                catch (Exception ex)
                {
                    MessagesTextBox.AppendText($"Connection attempt failed: {ex.Message}\n");
                }
            }
        }

        //private async void StartClient()
        //{
        //    if (pipeClient == null || !pipeClient.IsConnected)
        //    {
        //        pipeClient = new NamedPipeClientStream(".", "PipeChat", PipeDirection.InOut, PipeOptions.Asynchronous);
        //    }


        //    await pipeClient.ConnectAsync(1000); // 1 second timeout
        //    MessagesTextBox.AppendText("Connected to server.\n");
        //    reader = new StreamReader(pipeClient);
        //    writer = new StreamWriter(pipeClient) { AutoFlush = true };

        //    ListenForMessages();
        //}

        private async void ListenForMessages()
        {
            while (pipeClient.IsConnected)
            {
                string message = await reader.ReadLineAsync();
                Dispatcher.Invoke(() => MessagesTextBox.AppendText($"Server: {message}\n"));
            }
        }

        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (pipeClient != null && pipeClient.IsConnected)
            {
                string message = MessageTextBox.Text;
                await writer.WriteLineAsync(message);
                MessagesTextBox.AppendText($"Client: {message}\n");
                MessageTextBox.Clear();
            }
            else
            {
                MessagesTextBox.AppendText("Not connected to server.\n");
            }
        }

        private async void SendCommand(object sender, RoutedEventArgs e)
        {
            if (pipeClient != null && pipeClient.IsConnected)
            {
                string message = "Run Clashes";
                await writer.WriteLineAsync(message);
                MessagesTextBox.AppendText($"Client: {message}\n");
                MessageTextBox.Clear();
            }
            else
            {
                MessagesTextBox.AppendText("Not connected to server.\n");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose of the pipeClient and other resources
            if (pipeClient != null)
            {
                if (pipeClient.IsConnected)
                {
                    pipeClient.WaitForPipeDrain();
                }
                pipeClient.Dispose();
                pipeClient = null;
            }

            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }

            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }

            if (connectionTimer != null)
            {
                connectionTimer.Stop();
                connectionTimer = null;
            }
        }
    }
}