using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace Packl
{
    public enum InstallerType
    {
        Unknown,
        MSI,
        InnoSetup,
        NSIS,
        InstallShield,
        Wise
    }

    public static class InstallerInspector
    {
        /// <summary>
        /// Detecta o tipo de instalador com base no arquivo fornecido.
        /// </summary>
        /// <param name="filePath">Caminho completo para o instalador.</param>
        /// <returns>Tipo de instalador detectado.</returns>
        public static InstallerType DetectInstallerType(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Arquivo não encontrado", filePath);

            string ext = Path.GetExtension(filePath).ToLower();

            // Primeiro filtro por extensão
            if (ext == ".msi")
                return InstallerType.MSI;

            // Extrai strings ASCII do binário
            var strings = ExtractAsciiStrings(filePath);

            foreach (var str in strings)
            {
                if (str.Contains("Inno Setup"))
                    return InstallerType.InnoSetup;
                if (str.Contains("Nullsoft") || str.Contains("NSIS"))
                    return InstallerType.NSIS;
                if (str.Contains("InstallShield"))
                    return InstallerType.InstallShield;
                if (str.Contains("Wise"))
                    return InstallerType.Wise;
            }

            return InstallerType.Unknown;
        }

        private static IEnumerable<string> ExtractAsciiStrings(string filePath, int minLength = 4)
        {
            List<string> strings = new List<string>();
            byte[] data = File.ReadAllBytes(filePath);
            StringBuilder sb = new StringBuilder();

            foreach (byte b in data)
            {
                if (b >= 32 && b <= 126) // ASCII imprimível
                    sb.Append((char)b);
                else
                {
                    if (sb.Length >= minLength)
                        strings.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length >= minLength)
                strings.Add(sb.ToString());

            return strings;
        }
    }
}
