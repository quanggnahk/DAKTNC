using System;

namespace FPGA_UART_Dashboard.Modules
{
    public class BerAnalyzer
    {
        /// <summary>
        /// Tính toán số lượng bit lỗi (BER) bằng phép toán XOR.
        /// (Đáp ứng tiêu chí SW-07: Đếm bit lỗi, xử lý mất byte)
        /// </summary>
        /// <param name="txData">Mảng byte dữ liệu đã gửi đi</param>
        /// <param name="rxData">Mảng byte dữ liệu nhận về</param>
        /// <returns>Tổng số bit bị lỗi</returns>
        public static int CalculateBitErrors(byte[] txData, byte[] rxData)
        {
            int totalErrorBits = 0;

            // Xác định chiều dài mảng ngắn nhất để tránh lỗi tràn mảng (IndexOutOfRangeException)
            int minLength = Math.Min(txData.Length, rxData.Length);

            for (int i = 0; i < minLength; i++)
            {
                // Bước 1: Dùng phép XOR (^) giữa byte gửi và byte nhận.
                // Các bit giống nhau sẽ ra 0, khác nhau sẽ ra 1.
                byte xorResult = (byte)(txData[i] ^ rxData[i]);

                // Bước 2: Đếm số lượng bit 1 trong kết quả XOR và cộng dồn
                totalErrorBits += CountSetBits(xorResult);
            }

            // Ghi nhận thêm lỗi nếu độ dài 2 mảng không bằng nhau (Lỗi mất byte / Dư byte)
            // Mỗi byte bị chênh lệch sẽ được tính là 8 bit lỗi
            int lengthDifference = Math.Abs(txData.Length - rxData.Length);
            totalErrorBits += (lengthDifference * 8);

            return totalErrorBits;
        }

        /// <summary>
        /// Thuật toán Kernighan: Đếm nhanh số lượng bit '1' trong một byte.
        /// (Tối ưu hóa tốc độ xử lý khi test với mảng dữ liệu lớn)
        /// </summary>
        private static int CountSetBits(byte n)
        {
            int count = 0;
            while (n > 0)
            {
                n &= (byte)(n - 1); // Xóa bit '1' ở vị trí thấp nhất (LSB)
                count++;
            }
            return count;
        }
    }
}