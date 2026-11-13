using System;
using System.Threading.Tasks;

namespace FPGA_UART_Dashboard.Modules
{
    public class CommandHandler
    {
        private UartManager _uartManager;

        public CommandHandler(UartManager uartManager)
        {
            _uartManager = uartManager;
        }

        // Lệnh Test Ping-Pong
        public async Task<bool> TestConnectionAsync()
        {
            return await _uartManager.SendCommandAsync(
                txCommand: "ping\n",
                expectedRx: "pong",
                timeoutMs: 1000
            );
        }

        // Lệnh Bật LED
        public async Task<bool> TurnOnLedAsync()
        {
            return await _uartManager.SendCommandAsync(
                txCommand: "#SON*\n",
                expectedRx: "#LON*\n",
                timeoutMs: 1000
            );
        }

        // Lệnh Tắt LED
        public async Task<bool> TurnOffLedAsync()
        {
            return await _uartManager.SendCommandAsync(
                txCommand: "#SOF*\n",
                expectedRx: "#LOF*\n",
                timeoutMs: 1000
            );
        }

        // --- HÀM MỚI: TẠO VÀ GỬI DỮ LIỆU STRESS TEST ---
        public async Task<bool> SendStressTestDataAsync(int numBytes, string patternType)
        {
            byte[] txData = new byte[numBytes];
            Random rand = new Random();

            // Sinh dữ liệu dựa trên lựa chọn từ ComboBox
            switch (patternType)
            {
                case "Random":
                    rand.NextBytes(txData); // Đổ ngẫu nhiên vào mảng
                    break;
                case "0x55":
                    for (int i = 0; i < numBytes; i++) txData[i] = 0x55;
                    break;
                case "0xAA":
                    for (int i = 0; i < numBytes; i++) txData[i] = 0xAA;
                    break;
                case "Ascending":
                    for (int i = 0; i < numBytes; i++) txData[i] = (byte)(i % 256);
                    break;
            }

            // Gọi hàm gửi Byte thô bên UartManager (cài đặt Timeout chờ tối đa 5 giây)
            return await _uartManager.SendBytesAsync(txData, timeoutMs: 5000);
        }
    }
}