using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;

namespace GaussianSplatting.Runtime.Utils
{
    public class PlyReader
    {
        private static void ReadHeaderImpl(byte[] data, out int vertexCount, out int vertexStride, out List<string> attrNames)
        {
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if (data.Length >= 2 * 1024 * 1024 * 1024L)
                throw new IOException($"PLY read error: currently files larger than 2GB are not supported");
            // read header
            vertexCount = 0;
            vertexStride = 0;
            attrNames = new List<string>();
            string[] lines;
            try
            {
                lines = Encoding.UTF8.GetString(data).Split('\n');
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
            foreach (var line in lines)
            {
                if (line == "end_header" || line.Length == 0)
                    break;
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = int.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                {
                    ElementType type = tokens[1] switch
                    {
                        "float" => ElementType.Float,
                        "double" => ElementType.Double,
                        "uchar" => ElementType.UChar,
                        _ => ElementType.None
                    };
                    vertexStride += TypeToSize(type);
                    attrNames.Add(tokens[2]);
                }
            }
        }

        public static void ReadByteArray(byte[] data, out int vertexCount, out int vertexStride,
            out List<string> attrNames, out NativeArray<byte> vertices)
        {
            ReadHeaderImpl(data, out vertexCount, out vertexStride, out attrNames);
            vertices = new NativeArray<byte>(vertexCount * vertexStride, Allocator.Persistent);
            if (data.Length != vertices.Length)
                throw new IOException($"PLY read error, expected {vertices.Length} data bytes got {data.Length}");
        }

        public enum ElementType
        {
            None,
            Float,
            Double,
            UChar
        }

        public static int TypeToSize(ElementType t)
        {
            return t switch
            {
                ElementType.None => 0,
                ElementType.Float => 4,
                ElementType.Double => 8,
                ElementType.UChar => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(t), t, null)
            };
        }
    }
}