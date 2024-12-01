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
            StartClient();
        }

        private void StartClient()
        {
            pipeClient = new NamedPipeClientStream(".", "RevitUIConn", PipeDirection.InOut, PipeOptions.None);

            pipeClient.Connect(1000); // 1 second timeout
            MessagesTextBox.AppendText("Connected to server.\n");
            writer = new StreamWriter(pipeClient) { AutoFlush = true };
            reader = new StreamReader(pipeClient);

            Thread listenThread = new Thread(ListenForMessages);
            listenThread.Start();

        }

        private void ListenForMessages()
        {
            while (pipeClient.IsConnected)
            {
                string message = reader.ReadLine();
                MessagesTextBox.AppendText($"Server: {message}\n");
                //Dispatcher.Invoke(() => MessagesTextBox.AppendText($"Server: {message}\n"));
            }
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (pipeClient == null || !pipeClient.IsConnected)
            {
                StartClient();
            }

            if (pipeClient != null && pipeClient.IsConnected)
            {
                string message = MessageTextBox.Text;
                writer.WriteLine(message);
                MessagesTextBox.AppendText($"Client: {message}\n");
                MessageTextBox.Clear();
            }
            else
            {
                MessagesTextBox.AppendText("Not connected to server.\n");
            }
        }

        private void SendCommand(object sender, RoutedEventArgs e)
        {
            if (pipeClient != null && pipeClient.IsConnected)
            {
                string message = "Run Clashes";
                writer.WriteLine(message);
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