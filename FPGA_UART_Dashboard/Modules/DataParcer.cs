using System;
using System.Diagnostics;
using System.Linq;

namespace FPGA_UART_Dashboard.Modules
{
    public class DataParser
    {
        private const string PACKET_HEADER = "#S,";
        private const char CHECKSUM_SEPARATOR = '*';

        /// <summary>
        /// Hàm bóc tách gói tin dạng: #S,30,60,150*A5
        /// </summary>
        public static string[] ParseSensorData(string rawPacket)
        {
            try
            {
                // Kiểm tra định dạng cơ bản: Phải có Header và Dấu phân cách Checksum
                if (!rawPacket.StartsWith(PACKET_HEADER) || !rawPacket.Contains(CHECKSUM_SEPARATOR))
                {
                    return null;
                }

                // Tìm vị trí của dấu '*' để tách phần Data
                int checksumIndex = rawPacket.IndexOf(CHECKSUM_SEPARATOR);

                // Trích xuất phần dữ liệu (bỏ "#S," ở đầu và bỏ "*XX" ở cuối)
                string dataPayload = rawPacket.Substring(PACKET_HEADER.Length, checksumIndex - PACKET_HEADER.Length);

                // Cắt chuỗi thành mảng các giá trị dựa vào dấu phẩy
                string[] sensorValues = dataPayload.Split(',');

                return sensorValues;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Parser Error]: {ex.Message}");
                return null;
            }
        }
    }
}