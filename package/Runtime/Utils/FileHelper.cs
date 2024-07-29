using System;
using System.IO;

namespace GaussianSplatting.Runtime.Utils
{
    public static class FileHelper
    {
        public static byte[] ReadFileToByteArray(string filePath)
        {
            try
            {
                filePath = filePath.Replace("\"", "");
                string normalizedPath = Path.GetFullPath(filePath.Replace("//", Path.DirectorySeparatorChar.ToString()));
                // Check if the file exists at the given path
                if (!File.Exists(normalizedPath))
                {
                    Console.WriteLine("File does not exist.");
                    return null;
                }

                // Read all bytes from the file
                byte[] fileBytes = File.ReadAllBytes(normalizedPath);
                return fileBytes;
            }
            catch (Exception ex)
            {
                // Log any exceptions that occur during the read operation
                Console.WriteLine($"An error occurred: {ex.Message}");
                return null;
            }
        }
    }
}