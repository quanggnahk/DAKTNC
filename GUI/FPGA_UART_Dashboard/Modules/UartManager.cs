using System;
using System.Collections.Generic; // Bổ sung thư viện cho List<byte>
using System.Threading.Tasks;
using RJCP.IO.Ports;

namespace FPGA_UART_Dashboard.Modules
{
    public class UartManager : IDisposable
    {
        private SerialPortStream _serialPort;
        private string _dataBuffer = "";

        // ---- SỰ KIỆN GIAO TIẾP VỚI UI ----
        public event Action<string> OnLogMessage;
        public event Action<string[]> OnSensorDataReceived;

        // Sự kiện mới cho Stress Test & ProgressBar
        public event Action<int, int> OnProgressChanged;
        public event Action<byte[], byte[]> OnStressTestCompleted;

        public event Action OnDeviceDisconnected;

        // ---- BIẾN CHO GIAO THỨC LỆNH (STRING) ----
        private bool _isWaitingForAck = false;
        private string _expectedAck = "";
        private bool _ackReceived = false;

        // ---- BIẾN CHO STRESS TEST (BYTE) ----
        private bool _isStressTesting = false;
        private byte[] _expectedTxData;
        private List<byte> _rxBufferQueue = new List<byte>();
        private int _expectedRxCount = 0;

        public UartManager()
        {
            _serialPort = new SerialPortStream();
        }

        public string[] GetAvailablePorts()
        {
            return System.IO.Ports.SerialPort.GetPortNames();
        }

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public void Connect(string portName, int baudRate)
        {
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
            }

            _serialPort = new SerialPortStream
            {
                PortName = portName,
                BaudRate = baudRate,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();
        }

        public void Disconnect()
        {
            if (IsConnected)
            {
                _serialPort.Close();
            }
        }

        // HÀM 1: GỬI LỆNH ĐIỀU KHIỂN DẠNG STRING (Ping/Pong, Bật tắt LED)
        public async Task<bool> SendCommandAsync(string txCommand, string expectedRx, int timeoutMs = 1000)
        {
            if (!IsConnected) return false;

            _expectedAck = expectedRx;
            _ackReceived = false;
            _isWaitingForAck = true;

            string logTx = txCommand.Replace("\n", "\\n");
            OnLogMessage?.Invoke($"[Tx] {logTx}");

            _serialPort.Write(txCommand);

            int timeoutCounter = 0;
            int maxLoops = timeoutMs / 100;
            while (!_ackReceived && timeoutCounter < maxLoops)
            {
                await Task.Delay(100);
                timeoutCounter++;
            }

            _isWaitingForAck = false;
            return _ackReceived;
        }

        // HÀM 2: GỬI MẢNG BYTE THÔ CHO STRESS TEST (Cập nhật mới)
        public async Task<bool> SendBytesAsync(byte[] txData, int timeoutMs = 5000)
        {
            if (!IsConnected) return false;

            _isStressTesting = true;
            _expectedTxData = txData;
            _expectedRxCount = txData.Length;
            _rxBufferQueue.Clear();

            OnLogMessage?.Invoke($"[Tx] Bắt đầu Stress Test {txData.Length} bytes...");

            // Gửi toàn bộ mảng byte xuống cổng COM
            _serialPort.Write(txData, 0, txData.Length);

            int timeoutCounter = 0;
            int maxLoops = timeoutMs / 10; // Chờ mỗi 10ms để ProgressBar chạy mượt

            while (_rxBufferQueue.Count < _expectedRxCount && timeoutCounter < maxLoops)
            {
                // Kích hoạt event đẩy thông số lên ProgressBar
                OnProgressChanged?.Invoke(_rxBufferQueue.Count, _expectedRxCount);

                await Task.Delay(10);
                timeoutCounter++;
            }

            _isStressTesting = false;

            // Ép thanh ProgressBar đầy 100% khi xong
            OnProgressChanged?.Invoke(_rxBufferQueue.Count, _expectedRxCount);

            if (_rxBufferQueue.Count > 0)
            {
                // Đẩy mảng gửi và mảng nhận lên UI để phân tích BER
                OnStressTestCompleted?.Invoke(_expectedTxData, _rxBufferQueue.ToArray());
                return true;
            }

            return false;
        }

        // HÀM XỬ LÝ SỰ KIỆN NHẬN DỮ LIỆU ĐƯỢC CHIA LÀM 2 NHÁNH (BYTE VÀ STRING)
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (!IsConnected) return;

                // NHÁNH 1: Đang trong chế độ Stress Test -> Chỉ đọc Byte thô
                if (_isStressTesting)
                {
                    int bytesToRead = _serialPort.BytesToRead;
                    if (bytesToRead > 0)
                    {
                        byte[] buffer = new byte[bytesToRead];
                        _serialPort.Read(buffer, 0, bytesToRead);

                        // Đẩy byte vào hàng đợi (Queue)
                        _rxBufferQueue.AddRange(buffer);
                    }
                    return; // Bắt buộc return để không chạy xuống phần xử lý String bên dưới
                }

                // NHÁNH 2: Chế độ bình thường -> Đọc dạng String
                string incomingData = _serialPort.ReadExisting();
                _dataBuffer += incomingData;

                // 2.1. Quét chuỗi chờ phản hồi ACK (Ping-pong, báo LED)
                if (_isWaitingForAck && !string.IsNullOrEmpty(_expectedAck))
                {
                    if (_dataBuffer.Contains(_expectedAck))
                    {
                        _ackReceived = true;
                        string logRx = _expectedAck.Replace("\n", "\\n");
                        OnLogMessage?.Invoke($"[Rx] {logRx}");
                        _dataBuffer = _dataBuffer.Replace(_expectedAck, "");
                    }
                }

                // 2.2. Bóc tách dữ liệu gói tin Cảm biến
                if (_dataBuffer.Contains("\n"))
                {
                    string[] frames = _dataBuffer.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string frame in frames)
                    {
                        if (frame.StartsWith("#S,"))
                        {
                            string[] parsedValues = DataParser.ParseSensorData(frame);

                            if (parsedValues != null)
                            {
                                OnSensorDataReceived?.Invoke(parsedValues);
                            }

                            _dataBuffer = _dataBuffer.Replace(frame + "\n", "");
                        }
                    }
                }
            }
            catch(Exception) {
                if (IsConnected)
                {
                    try { _serialPort.Close(); } catch { }
                }

                // Bắn sự kiện mất kết nối về giao diện MainWindow để cập nhật UI an toàn
                OnDeviceDisconnected?.Invoke();
            }
        }

        public void Dispose()
        {
            Disconnect();
            if (_serialPort != null)
            {
                _serialPort.Dispose();
            }
        }
    }
}