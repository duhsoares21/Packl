using Packl.Classes;
using System;

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Packl
{
    enum Action{
        INSTALL,
        UNINSTALL,
        INSTALL_DEPENDENCY
    }

    class Program
    {
        public static string InstallFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"\\packl\\apps\\";
        public static readonly string AliasesFolder = Path.Combine(InstallFolder, ".aliases");

        static async Task Main(string[] args)
        {
            string command;
            string package;

            if (args.Length == 0) {
                return;
            }

            command = args[0];
            package = args[1];

            switch (command)
            {
                case "install":
                    await Install(package);
                    break;
                case "uninstall":
                    await Uninstall(package);
                    break;
                case "update":
                    await Update(package);
                    break;
            }
        }

        static async Task CheckDependencies(HttpClient client, PackageFormat package)
        {
            Console.WriteLine($"Verificando dependências. Aguarde...");
            
            if (package.dependencies == null || package.dependencies.Count <= 0) {
                Console.WriteLine($"Nenhuma dependência encontrada. Continuando...");
                return;
            }

            foreach (string dependency in package.dependencies)
            {
                Console.WriteLine($"Baixando dependência em {dependency}. Aguarde...");
                string downloadedDepencency = await Download(client, dependency);
                Console.WriteLine($"Instalando dependência {downloadedDepencency}. Aguarde...");

                InstallerType installerType = InstallerInspector.DetectInstallerType(downloadedDepencency);

                string arguments = string.Empty;

                switch (installerType)
                {
                    case InstallerType.MSI:
                        arguments = $"/quiet";
                        break;
                    case InstallerType.InnoSetup:
                        arguments = $"/VERYSILENT";
                        break;
                    case InstallerType.NSIS:
                        arguments = $"/S";
                        break;
                    case InstallerType.InstallShield:
                        arguments = $"/silent";
                        break;
                    case InstallerType.Wise:
                        arguments = $"/s";
                        break;
                    case InstallerType.Unknown:
                        arguments = $"/S";
                        break;
                    default:
                        arguments = $"/S";
                        break;
                }

                await RunInstallerAsync(downloadedDepencency, arguments, Action.INSTALL_DEPENDENCY, installerType);

                Console.WriteLine($"Dependência {downloadedDepencency} instalada com sucesso.");
                File.Delete(downloadedDepencency);
            }

            Console.WriteLine("Todas as dependências foram instaladas.");
        }

        static async Task<bool> VerifyChecksum(string filePath, string expectedHash)
        {
            Console.WriteLine("Verificando Checksum...");
            if (!File.Exists(filePath))
                Console.WriteLine("Arquivo não encontrado.");

            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();

            var hashBytes = sha256.ComputeHash(stream);

            var fileHash = await Task.Run(() => BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant());

            Console.WriteLine($"Expected: {expectedHash}");
            Console.WriteLine($"Actual:   {fileHash}");

            return fileHash == expectedHash;
        }

        static async Task Install(string packageName)
        {
            string BaseURL = "https://raw.githubusercontent.com/duhsoares21/packably/main/packages/";
            string extension = ".json";

            string packageURL = BaseURL + packageName + extension;

            string appFolder = InstallFolder + packageName;

            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler);

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Packl-App/1.0");

            Console.WriteLine("Conectando ao GitHub...");

            string json = "";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl",
                Arguments = $"-L {packageURL}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string errors = await process.StandardError.ReadToEndAsync();

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    json = output;
                }
                else
                {
                    Console.WriteLine($"Curl falhou com código {process.ExitCode}");
                    Console.WriteLine(errors);
                }
            }

            Console.WriteLine("Conectado!\n");
            Console.WriteLine("Aguardando JSON...");

            var package = JsonDocument.Parse(json).Deserialize<PackageFormat>();

            Console.WriteLine("JSON Pronto!\n");
            Console.WriteLine("Baixando Pacote...");

            string installerPath = await Download(client, package.url);

            bool isValidPackage = await VerifyChecksum(installerPath, package.hash);

            if (isValidPackage)
            {
                Console.WriteLine("Checksum Válido. Prosseguindo...");
            }
            else
            {
                Console.WriteLine($"Pacote {installerPath} Inválido. Checksum não corresponde.");
                File.Delete(installerPath);
                return;
            }

            await CheckDependencies(client, package);

            switch (package.type)
            {
                case "executable":
                    await Executable(package, installerPath, appFolder);
                    break;
                case "zip":
                    await Zip(installerPath, appFolder);
                    break;
                case "portable":
                    await Portable(installerPath, appFolder);
                    break;
            }

            string executableName = package.bin ?? Path.GetFileName(appFolder);

            if (!string.IsNullOrEmpty(executableName))
            {
                string executablePath = Path.Combine(appFolder, executableName);
                await CreateAliasBatchFile(packageName, executablePath);
                await AddToPath(AliasesFolder);
                Console.WriteLine($"Alias '{packageName}' criado para {executablePath}.");
            }
            else
            {
                Console.WriteLine($"Aviso: Nenhum executável encontrado para criar alias para {packageName}.");
            }
        }

        static async Task CreateAliasBatchFile(string alias, string executablePath)
        {
            try
            {
                // Validate executable exists
                if (!File.Exists(executablePath))
                {
                    Console.WriteLine($"Erro: Executável {executablePath} não encontrado.");
                    return;
                }

                // Create aliases folder if it doesn't exist
                Directory.CreateDirectory(AliasesFolder);

                // Create batch file content
                string batchContent = $"@echo off\n\"{executablePath}\" %*";
                string batchFilePath = Path.Combine(AliasesFolder, $"{alias}.bat");

                // Write batch file
                await Task.Run(() => File.WriteAllText(batchFilePath, batchContent));
                Console.WriteLine($"Arquivo de alias criado: {batchFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao criar alias '{alias}': {ex.Message}");
            }
        }

        static async Task AddToPath(string path)
        {
            try
            {
                // Get the current user's PATH environment variable
                string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";

                // Check if the path is already in PATH to avoid duplicates
                if (!currentPath.Split(';').Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    // Append the new path
                    string newPath = currentPath.EndsWith(";") ? $"{currentPath}{path}" : $"{currentPath};{path}";

                    // Set the updated PATH for the user
                    await Task.Run(() => Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User));
                    Console.WriteLine($"Caminho {path} adicionado ao PATH do usuário com sucesso.");
                }
                else
                {
                    Console.WriteLine($"Caminho {path} já está no PATH do usuário.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao adicionar {path} ao PATH: {ex.Message}");
            }
        }

        static async Task<string> Download(HttpClient client, string url)
        {
            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            string[] urlDownloadSplit = url.Split('/');
            string downloadFileName = urlDownloadSplit[urlDownloadSplit.Length - 1];

            string downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"
            );

            string installerPath = Path.Combine(downloadsPath, downloadFileName);

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int read;
                var lastProgress = 0;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    if (canReportProgress)
                    {
                        var progress = (int)((totalRead * 100) / totalBytes);
                        if (progress != lastProgress)
                        {
                            Console.Write($"\rProgresso: {progress}%   ");
                            lastProgress = progress;
                        }
                    }
                    else
                    {
                        Console.Write($"\rBaixados: {totalRead / 1024} KB   ");
                    }
                }
            }

            Console.WriteLine($"\nInstalador baixado em {installerPath}\n");

            return installerPath;
        }

        static async Task Executable(PackageFormat package, string installerPath, string appFolder)
        {
            Console.WriteLine($"Iniciando instalação. Aguarde...");
            InstallerType installerType = InstallerInspector.DetectInstallerType(installerPath);

            string arguments = string.Empty;

            switch (installerType)
            {
                case InstallerType.MSI:
                    arguments = $"/quiet INSTALLDIR={appFolder} TARGETDIR={appFolder} INSTALL_ROOT={appFolder} INSTALLLOCATION={appFolder} APPDIR={appFolder}";
                    break;
                case InstallerType.InnoSetup:
                    arguments = $"/VERYSILENT /DIR={appFolder}";
                    break;
                case InstallerType.NSIS:
                    arguments = $"/S /D={appFolder}";
                    break;
                case InstallerType.InstallShield:
                    arguments = $"/silent /D={appFolder}";
                    break;
                case InstallerType.Wise:
                    arguments = $"/s /d={appFolder}";
                    break;
                case InstallerType.Unknown:
                    arguments = $"/S /D={appFolder}";
                    break;   
                default:
                    arguments = $"/S /D={appFolder}";
                    break;
            }

            await RunInstallerAsync(installerPath, arguments, Action.INSTALL, installerType); // Fallback arguments

        }

        static async Task Zip(string installerPath, string appFolder)
        {
            Console.WriteLine($"Iniciando extração do zip. Aguarde...");
            await Task.Run(() => ZipFile.ExtractToDirectory(installerPath, appFolder));
            Console.WriteLine($"Extraído com sucesso.");
        }

        static async Task Portable(string installerPath, string appFolder)
        {
            Console.WriteLine($"Copiando aplicação. Aguarde...");
            Directory.CreateDirectory(appFolder);
            string destinationFile = Path.Combine(appFolder, Path.GetFileName(installerPath));

            await Task.Run(() => File.Copy(installerPath, destinationFile, overwrite: true));
            Console.WriteLine($"Copiado com sucesso.");
        }

        static async Task Uninstall(string packageName)
        {

            if (!Directory.Exists(InstallFolder + packageName))
            {
                Console.WriteLine($"Pacote {packageName} não está instalado.");
                return;
            }

            Console.WriteLine("Iniciando desinstalação. Aguarde...");

            string psCommand =
            $"Get-ItemProperty 'HKLM:Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*' " +
            $"| Where-Object {{ $_.UninstallString -like '*{packageName}*' }} " +
            "| Select-Object DisplayName, DisplayVersion, Publisher, InstallDate, UninstallString" +
            "| ConvertTo-Json -Compress";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                
                process.WaitForExit();
                
                if (!string.IsNullOrEmpty(errors))
                {
                    Console.WriteLine("Erros:");
                    Console.WriteLine(errors);
                }

                if (output == string.Empty)
                {
                    string DeleteFolder = InstallFolder + packageName;
                    Directory.Delete(DeleteFolder, recursive: true);
                    Console.WriteLine("App desinstalado. Pasta removida com sucesso!");
                }
                else
                {
                    InstalledProgram program = JsonSerializer.Deserialize<InstalledProgram>(output);
                    string uninstallPath = program.UninstallString.Trim('"');

                    InstallerType installerType = InstallerInspector.DetectInstallerType(uninstallPath);

                    string arguments = string.Empty;

                    switch (installerType)
                    {
                        case InstallerType.MSI:
                            arguments = $"/quiet";
                            break;
                        case InstallerType.InnoSetup:
                            arguments = $"/VERYSILENT";
                            break;
                        case InstallerType.NSIS:
                            arguments = $"/S";
                            break;
                        case InstallerType.InstallShield:
                            arguments = $"/silent";
                            break;
                        case InstallerType.Wise:
                            arguments = $"/s";
                            break;
                        case InstallerType.Unknown:
                            arguments = $"/S";
                            break;
                        default:
                            arguments = $"/S";
                            break;
                    }

                    await RunInstallerAsync(uninstallPath, arguments, Action.UNINSTALL, installerType);
                }

            }
        }

        static async Task Update(string packageName)
        {
            await Uninstall(packageName);
            await Install(packageName);
        }

        static async Task RunInstallerAsync(string installerPath, string arguments, Action action, InstallerType installerType)
        {
            if (!File.Exists(installerPath))
            {
                Console.WriteLine($"Instalador não encontrado em {installerPath}");
                return;
            }

            ProcessStartInfo psi = new ProcessStartInfo();

            if(installerType == InstallerType.MSI)
            {
                string msiArguments = $"/i \"{installerPath}\" {arguments}";

                psi.FileName = "msiexec.exe";
                psi.Arguments = msiArguments;
                psi.UseShellExecute = false;
            }
            else
            {
                psi.FileName = installerPath;
                psi.Arguments = arguments;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
            }

            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.CreateNoWindow = false;
            psi.WorkingDirectory = Path.GetDirectoryName(installerPath);

            Process process = new Process { StartInfo = psi };

            process.Start();

            await Task.Run(() => process.WaitForExit()); // async wait

            string message = "";

            switch (action)
            {
                case Action.INSTALL:
                    message = "App Instalado com sucesso";
                    break;
                case Action.UNINSTALL:
                    message = "App Desinstalado com sucesso";
                    break;
                case Action.INSTALL_DEPENDENCY:
                    message = "Dependência instalada com sucesso";
                    break;
            }

            Console.WriteLine(process.ExitCode == 0 ? message : $"Erro. Tipo: {action}. Código: {process.ExitCode}");
        }
    }
}
