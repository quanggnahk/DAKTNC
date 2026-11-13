using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using FPGA_UART_Dashboard.Modules;
using Microsoft.Win32; // Dùng cho SaveFileDialog
using System.IO;

namespace FPGA_UART_Dashboard
{
    public partial class MainWindow : Window
    {
        private UartManager _uart;
        private CommandHandler _cmdHandler;
        private Stopwatch _stressTestWatch; // Dùng để đo tốc độ truyền byte/s

        public MainWindow()
        {
            InitializeComponent();

            _uart = new UartManager();
            _cmdHandler = new CommandHandler(_uart);

            // Đăng ký các Event nhận dữ liệu và tiến trình từ UartManager
            _uart.OnLogMessage += Uart_OnLogMessage;
            _uart.OnSensorDataReceived += Uart_OnSensorDataReceived;
            _uart.OnProgressChanged += Uart_OnProgressChanged;
            _uart.OnStressTestCompleted += Uart_OnStressTestCompleted;
            _uart.OnDeviceDisconnected += Uart_OnDeviceDisconnected;

            LoadAvailablePorts();
        }

        private void LoadAvailablePorts()
        {
            string[] ports = _uart.GetAvailablePorts();
            cmbPorts.ItemsSource = ports;

            if (ports.Length > 0) cmbPorts.SelectedIndex = 0;
            else MessageBox.Show("Không tìm thấy cổng COM nào!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---- CÁC HÀM CẬP NHẬT UI TỪ BACKGROUND THREAD ----

        private void Uart_OnLogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtConsole.AppendText($"{message}\n");
                txtConsole.ScrollToEnd();
            });
        }

        private void Uart_OnSensorDataReceived(string[] sensorValues)
        {
            Dispatcher.Invoke(() =>
            {
                if (sensorValues.Length >= 3)
                {
                    txtTemp.Text = sensorValues[0];
                    txtHumid.Text = sensorValues[1];
                    txtSensor3.Text = sensorValues[2];

                    txtConsole.AppendText($"[DATA PARSED] T: {sensorValues[0]} | H: {sensorValues[1]} | D: {sensorValues[2]}\n");
                    txtConsole.ScrollToEnd();
                }
            });
        }

        private void Uart_OnProgressChanged(int current, int total)
        {
            Dispatcher.Invoke(() =>
            {
                prgStressTest.Maximum = total;
                prgStressTest.Value = current;
            });
        }

        private void Uart_OnStressTestCompleted(byte[] txData, byte[] rxData)
        {
            Dispatcher.Invoke(() =>
            {
                // Gọi thuật toán tính BER từ class BerAnalyzer
                int errorBits = BerAnalyzer.CalculateBitErrors(txData, rxData);
                int totalBits = txData.Length * 8;
                double ber = (double)errorBits / totalBits;

                txtErrorBits.Text = $"Số bit lỗi: {errorBits} / {totalBits} bits (BER: {ber:P4})";

                if (txData.Length != rxData.Length)
                {
                    txtErrorBits.Text += $"\n[CẢNH BÁO: Gửi {txData.Length} bytes, Nhận {rxData.Length} bytes]";
                    txtErrorBits.Foreground = Brushes.Red;
                }
                else if (errorBits == 0)
                {
                    txtErrorBits.Foreground = Brushes.Green;
                }
                else
                {
                    txtErrorBits.Foreground = Brushes.Orange;
                }
            });
        }

        // HÀM XỬ LÝ KHI PHẦN CỨNG BỊ RÚT ĐỘT NGỘT (SW-05)
        private void Uart_OnDeviceDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                // Đưa giao diện về trạng thái ngắt kết nối một cách an toàn
                btnConnect.Content = "Connect";
                cmbPorts.IsEnabled = true;
                cmbBaudrate.IsEnabled = true;
                txtStatus.Text = "Disconnected (Cáp bị ngắt đột ngột!)";
                txtStatus.Foreground = Brushes.Red;

                // In cảnh báo đỏ lên Console Log
                txtConsole.AppendText("[CRITICAL ERROR] Thiết bị phần cứng đã bị ngắt kết nối đột ngột!\n");
                txtConsole.ScrollToEnd();

                // Hiển thị Popup thông báo cho người dùng
                MessageBox.Show("Mất kết nối với phần cứng (Đã rút cáp)!", "Cảnh báo phần cứng", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        // ---- CÁC HÀM XỬ LÝ SỰ KIỆN NÚT BẤM ----

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnect.Content.ToString() == "Connect")
            {
                if (cmbPorts.SelectedItem == null || cmbBaudrate.SelectedItem == null)
                {
                    MessageBox.Show("Vui lòng chọn cổng COM và Baudrate!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    string selectedPort = cmbPorts.SelectedItem.ToString();
                    int baudrate = int.Parse(((ComboBoxItem)cmbBaudrate.SelectedItem).Content.ToString());

                    _uart.Connect(selectedPort, baudrate);

                    btnConnect.Content = "Disconnect";
                    cmbPorts.IsEnabled = false;
                    cmbBaudrate.IsEnabled = false;
                    txtStatus.Text = $"Connected {selectedPort} @ {baudrate} bps";
                    txtStatus.Foreground = Brushes.Green;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                _uart.Disconnect();
                btnConnect.Content = "Connect";
                cmbPorts.IsEnabled = true;
                cmbBaudrate.IsEnabled = true;
                txtStatus.Text = "Disconnected";
                txtStatus.Foreground = Brushes.Red;
            }
        }

        private async void btnTestConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!_uart.IsConnected) return;

            btnTestConnect.IsEnabled = false;
            bool success = await _cmdHandler.TestConnectionAsync();

            if (success) MessageBox.Show("Kết nối thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            else MessageBox.Show("Time-out 1s!\nKhông nhận được phản hồi 'pong'.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);

            btnTestConnect.IsEnabled = true;
        }

        private async void btnLedOn_Click(object sender, RoutedEventArgs e)
        {
            if (!_uart.IsConnected) return;
            btnLedOn.IsEnabled = false;

            bool success = await _cmdHandler.TurnOnLedAsync();
            if (!success) MessageBox.Show("Time-out!\nKhông nhận được phản hồi '#LON*'.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);

            btnLedOn.IsEnabled = true;
        }

        private async void btnLedOff_Click(object sender, RoutedEventArgs e)
        {
            if (!_uart.IsConnected) return;
            btnLedOff.IsEnabled = false;

            bool success = await _cmdHandler.TurnOffLedAsync();
            if (!success) MessageBox.Show("Time-out!\nKhông nhận được phản hồi '#LOF*'.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);

            btnLedOff.IsEnabled = true;
        }

        // HÀM XỬ LÝ STRESS TEST MỚI
        private async void btnStressTest_Click(object sender, RoutedEventArgs e)
        {
            if (!_uart.IsConnected)
            {
                MessageBox.Show("Vui lòng kết nối phần cứng trước!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtNumBytes.Text, out int numBytes) || numBytes <= 0)
            {
                MessageBox.Show("Số lượng Byte không hợp lệ!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string selectedPattern = ((ComboBoxItem)cmbPattern.SelectedItem).Content.ToString();

            // Khởi tạo trạng thái giao diện trước khi Test
            btnStressTest.IsEnabled = false;
            prgStressTest.Value = 0;
            txtBerResult.Text = "Trạng thái: Đang gửi và chờ phản hồi...";
            txtBerResult.Foreground = Brushes.Orange;
            txtSpeed.Text = "Tốc độ: Đang đo...";
            txtErrorBits.Text = "Số bit lỗi: 0 / 0";
            txtErrorBits.Foreground = Brushes.Black;

            // Bắt đầu đếm thời gian
            _stressTestWatch = Stopwatch.StartNew();

            // Chờ quá trình gửi và nhận qua UartManager
            bool hasResponse = await _cmdHandler.SendStressTestDataAsync(numBytes, selectedPattern);

            // Dừng đếm thời gian
            _stressTestWatch.Stop();
            double timeSeconds = _stressTestWatch.ElapsedMilliseconds / 1000.0;

            if (!hasResponse)
            {
                txtBerResult.Text = "Trạng thái: Time-out! Không nhận đủ dữ liệu dội về.";
                txtBerResult.Foreground = Brushes.Red;
            }
            else
            {
                txtBerResult.Text = "Trạng thái: Hoàn thành Stress Test!";
                txtBerResult.Foreground = Brushes.Green;

                // Cập nhật tốc độ truyền thực tế
                if (timeSeconds > 0)
                {
                    double speed = numBytes / timeSeconds;
                    txtSpeed.Text = $"Tốc độ: {speed:F2} Bytes/s";
                }
            }

            btnStressTest.IsEnabled = true;
        }
        private void btnExportLog_Click(object sender, RoutedEventArgs e)
        {
            // 1. Khởi tạo hộp thoại lưu file
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            // 2. Cấu hình định dạng file cho phép lưu (.txt hoặc .csv)
            saveFileDialog.Filter = "Text file (*.txt)|*.txt|CSV file (*.csv)|*.csv";
            saveFileDialog.Title = "Lưu kết quả kiểm thử và Log";

            // Tự động tạo tên file theo ngày giờ thực (Ví dụ: UART_Log_20261106_153000.txt)
            saveFileDialog.FileName = $"UART_Log_{DateTime.Now:yyyyMMdd_HHmmss}";

            // 3. Nếu người dùng nhấn OK trên hộp thoại
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Lấy nội dung hiện tại trên Console
                    string logContent = txtConsole.Text;

                    // Tổng hợp thông số Stress Test (BER)
                    string stressTestResult = $"=== KẾT QUẢ ĐO LƯỜNG STRESS TEST ===\n" +
                                              $"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                                              $"Trạng thái: {txtBerResult.Text}\n" +
                                              $"{txtErrorBits.Text}\n" +
                                              $"{txtSpeed.Text}\n" +
                                              $"====================================\n\n";

                    // Gộp dữ liệu Stress Test và Console Log
                    string finalContent = stressTestResult + "=== NHẬT KÝ TRUYỀN NHẬN (CONSOLE) ===\n" + logContent;

                    // Xử lý đặc biệt nếu người dùng chọn lưu file .CSV
                    if (saveFileDialog.FileName.EndsWith(".csv"))
                    {
                        // Thay thế các khoảng trắng/tab bằng dấu phẩy để định dạng cột CSV
                        finalContent = finalContent.Replace(" | ", ",");
                        finalContent = finalContent.Replace(": ", ",");
                    }

                    // 4. Ghi toàn bộ nội dung xuống file vật lý
                    File.WriteAllText(saveFileDialog.FileName, finalContent);

                    MessageBox.Show($"Đã xuất file thành công tại:\n{saveFileDialog.FileName}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Có lỗi xảy ra khi lưu file:\n{ex.Message}", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _uart?.Dispose();
            base.OnClosed(e);
        }
    }
}