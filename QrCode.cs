using QRCoder;

namespace MicroSIPRemote
{
    internal static class QrCode
    {
        public static bool[,] Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            using var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            int size = data.ModuleMatrix.Count;
            var matrix = new bool[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    matrix[r, c] = data.ModuleMatrix[r][c];
            return matrix;
        }
    }
}
