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

        public MainWindow()
        {
            InitializeComponent();
            //StartClient();
        }

        private async void StartClient()
        {
            pipeClient = new NamedPipeClientStream(".", "PipeChat", PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(1000); // 1 second timeout
            MessagesTextBox.AppendText("Connected to server.\n");
            reader = new StreamReader(pipeClient);
            writer = new StreamWriter(pipeClient) { AutoFlush = true };

            ListenForMessages();
        }

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
    }
}