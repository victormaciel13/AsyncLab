// Program.cs
using System;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace MunProc
{
    internal static class Program
    {
        // =================== Configuração ===================
        private const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
        private const string DATA_DIR_NAME = "dados_receita";
        private const string OUT_DIR_NAME = "mun_por_uf";
        private const string DIFF_DIR_NAME = "diffs";

        private static string FormatTempo(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private static void Main(string[] args)
        {
            var sw = Stopwatch.StartNew();

            // =================== Paths ===================
            string baseDir = Directory.GetCurrentDirectory();
            string dataRoot = Path.Combine(baseDir, DATA_DIR_NAME);
            string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);
            string diffRoot = Path.Combine(baseDir, DIFF_DIR_NAME);

            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(outRoot);
            Directory.CreateDirectory(diffRoot);

            string baseCsvPath = Path.Combine(dataRoot, "municipios_base.csv");
            string tempCsvPath = Path.Combine(dataRoot, "municipios_tmp.csv");

            // =================== 1) Verificar base e baixar ===================
            Console.WriteLine("== Verificação de arquivo base ==");
            if (!File.Exists(baseCsvPath))
            {
                Console.WriteLine("Base local não encontrada. Baixando e salvando como base...");
                BaixarCsv(CSV_URL, baseCsvPath, Encoding.UTF8);
                Console.WriteLine($"Base salva em: {baseCsvPath}");
            }
            else
            {
                Console.WriteLine("Base local encontrada.");
                Console.WriteLine("Baixando CSV temporário para comparar com a base...");
                BaixarCsv(CSV_URL, tempCsvPath, Encoding.UTF8);

                Console.WriteLine("Comparando arquivos (base x temporário)...");
                var (add, rem, chg) = CompararCsvs(baseCsvPath, tempCsvPath);

                if (add.Count == 0 && rem.Count == 0 && chg.Count == 0)
                {
                    Console.WriteLine("Nenhuma diferença detectada. Mantendo base atual.");
                    TryDelete(tempCsvPath);
                }
                else
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string diffPath = Path.Combine(diffRoot, $"diff_{stamp}.csv");
                    SalvarDiffCsv(diffPath, add, rem, chg);
                    Console.WriteLine($"Diferenças encontradas. Arquivo gerado: {diffPath}");

                    // Se quiser atualizar a base automaticamente, descomente a linha abaixo:
                    // File.Copy(tempCsvPath, baseCsvPath, overwrite: true);

                    TryDelete(tempCsvPath);
                }
            }

            // =================== 2) Ler/Parsear base corrente ===================
            Console.WriteLine("\nLendo e parseando a base...");
            var municipios = LerMunicipios(baseCsvPath);
            Console.WriteLine($"Registros lidos: {municipios.Count}");

            var municipiosValidos = municipios
                .Where(m => !string.Equals(m.Uf, "EX", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // =================== 3) Agrupar por UF e salvar BIN/CSV/JSON ===================
            Console.WriteLine("\nGerando saídas por UF (BIN/CSV/JSON)...");
            var porUf = municipiosValidos
                .GroupBy(m => m.Uf, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            int ufCount = 0;
            foreach (var grupo in porUf)
            {
                string uf = grupo.Key.ToUpperInvariant();
                var lista = grupo.OrderBy(m => m.NomePreferido, StringComparer.OrdinalIgnoreCase).ToList();
                ufCount++;

                Console.WriteLine($"UF {uf}: {lista.Count} municípios");

                // CSV
                string csvPath = Path.Combine(outRoot, $"municipios_{uf}.csv");
                using (var fs = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var swOut = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    swOut.WriteLine("TOM;IBGE;NomeTOM;NomeIBGE;UF");
                    foreach (var m in lista)
                        swOut.WriteLine($"{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf}");
                }

                // JSON
                string jsonPath = Path.Combine(outRoot, $"municipios_{uf}.json");
                File.WriteAllText(jsonPath,
                    JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);

                // BIN
                string binPath = Path.Combine(outRoot, $"municipios_{uf}.bin");
                using (var fs = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
                {
                    // Formato:
                    // [int32] count
                    // para cada item: Write(string) para Tom, Ibge, NomeTom, NomeIbge, Uf
                    bw.Write(lista.Count);
                    foreach (var m in lista)
                    {
                        bw.Write(m.Tom ?? "");
                        bw.Write(m.Ibge ?? "");
                        bw.Write(m.NomeTom ?? "");
                        bw.Write(m.NomeIbge ?? "");
                        bw.Write(m.Uf ?? "");
                    }
                }
            }

            sw.Stop();
            Console.WriteLine("\n===== RESUMO =====");
            Console.WriteLine($"UFs geradas: {ufCount}");
            Console.WriteLine($"Pasta base de dados: {dataRoot}");
            Console.WriteLine($"Pasta de saída: {outRoot}");
            Console.WriteLine($"Pasta de diffs: {diffRoot}");
            Console.WriteLine($"Tempo total: {FormatTempo(sw.ElapsedMilliseconds)}");

            // =================== 4) Pesquisa interativa ===================
            Console.WriteLine("\n== Pesquisa ==");
            Console.WriteLine("Comandos:");
            Console.WriteLine("  uf SP                -> lista todos da UF");
            Console.WriteLine("  nome <parte>         -> busca por parte do nome (TOM/IBGE)");
            Console.WriteLine("  cod <IBGE|TOM>       -> busca por código exato (ex: 3550308)");
            Console.WriteLine("  sair                 -> encerra");

            while (true)
            {
                Console.Write("\n> ");
                var line = (Console.ReadLine() ?? "").Trim();
                if (string.Equals(line, "sair", StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1].Trim() : "";

                IEnumerable<Municipio> results = Enumerable.Empty<Municipio>();

                switch (cmd)
                {
                    case "uf":
                        if (arg.Length == 2)
                            results = municipiosValidos.Where(m => string.Equals(m.Uf, arg, StringComparison.OrdinalIgnoreCase));
                        break;

                    case "nome":
                        if (!string.IsNullOrWhiteSpace(arg))
                        {
                            results = municipiosValidos.Where(m =>
                                (m.NomeTom ?? "").Contains(arg, StringComparison.OrdinalIgnoreCase) ||
                                (m.NomeIbge ?? "").Contains(arg, StringComparison.OrdinalIgnoreCase));
                        }
                        break;

                    case "cod":
                        if (!string.IsNullOrWhiteSpace(arg))
                            results = municipiosValidos.Where(m =>
                                string.Equals(m.Ibge, arg, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(m.Tom, arg, StringComparison.OrdinalIgnoreCase));
                        break;

                    default:
                        Console.WriteLine("Comando inválido.");
                        continue;
                }

                var list = results.Take(200).ToList();
                if (list.Count == 0) { Console.WriteLine("Nenhum resultado."); continue; }

                foreach (var m in list)
                    Console.WriteLine($"{m.Uf,-2} | {m.Ibge,-7} | {m.Tom,-6} | {m.NomePreferido}");

                if (list.Count == 200)
                    Console.WriteLine("(exibindo apenas os 200 primeiros)");
            }
        }

        // =================== Funções auxiliares ===================
        private static void BaixarCsv(string url, string destino, Encoding enc)
        {
            // Força TLS 1.2+ em ambientes antigos
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; // 3072 ~ Tls13

            using var wc = new WebClient { Encoding = enc };
            wc.DownloadFile(url, destino);
        }

        private static List<Municipio> LerMunicipios(string csvPath)
        {
            // Tenta UTF-8; se detectar caracteres substitutos, cai para Latin1
            string[] linhas;
            try
            {
                linhas = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (linhas.Length > 0 && linhas[0].Contains('�'))
                    linhas = File.ReadAllLines(csvPath, Encoding.Latin1);
            }
            catch
            {
                linhas = File.ReadAllLines(csvPath, Encoding.Latin1);
            }

            int startIndex = 0;
            if (linhas.Length > 0 &&
                (linhas[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 linhas[0].IndexOf("UF", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                startIndex = 1; // pula cabeçalho
            }

            var lista = new List<Municipio>(Math.Max(0, linhas.Length - startIndex));
            for (int i = startIndex; i < linhas.Length; i++)
            {
                var linha = (linhas[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(linha)) continue;

                var parts = linha.Split(';');
                if (parts.Length < 5) continue;

                var m = new Municipio
                {
                    Tom = Util.San(parts[0]),
                    Ibge = Util.San(parts[1]),
                    NomeTom = Util.San(parts[2]),
                    NomeIbge = Util.San(parts[3]),
                    Uf = Util.San(parts[4]).ToUpperInvariant()
                };
                lista.Add(m);
            }
            return lista;
        }

        private static (List<Municipio> add, List<Municipio> rem, List<(Municipio Old, Municipio New)> chg)
            CompararCsvs(string baseCsv, string novoCsv)
        {
            var baseList = LerMunicipios(baseCsv);
            var novoList = LerMunicipios(novoCsv);

            static string Key(Municipio m) =>
                string.IsNullOrWhiteSpace(m.Ibge) ? $"T:{m.Tom}" : $"I:{m.Ibge}";

            var baseMap = baseList.GroupBy(m => Key(m)).ToDictionary(g => g.Key, g => g.First());
            var novoMap = novoList.GroupBy(m => Key(m)).ToDictionary(g => g.Key, g => g.First());

            var add = new List<Municipio>();
            var rem = new List<Municipio>();
            var chg = new List<(Municipio Old, Municipio New)>();

            foreach (var kv in novoMap)
            {
                if (!baseMap.TryGetValue(kv.Key, out var oldM))
                {
                    add.Add(kv.Value);
                }
                else
                {
                    if (!Municipio.EqualsSemChave(oldM, kv.Value))
                        chg.Add((oldM, kv.Value));
                }
            }
            foreach (var kv in baseMap)
            {
                if (!novoMap.ContainsKey(kv.Key))
                    rem.Add(kv.Value);
            }
            return (add, rem, chg);
        }

        private static void SalvarDiffCsv(
            string path,
            List<Municipio> add,
            List<Municipio> rem,
            List<(Municipio Old, Municipio New)> chg)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false));

            sw.WriteLine("Tipo;TOM;IBGE;NomeTOM;NomeIBGE;UF;Obs");

            foreach (var m in add)
                sw.WriteLine($"ADICAO;{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf};");

            foreach (var m in rem)
                sw.WriteLine($"REMOCAO;{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf};");

            foreach (var (oldM, newM) in chg)
            {
                string diffs = oldM.DiffCampos(newM);
                sw.WriteLine($"ALTERACAO;{newM.Tom};{newM.Ibge};{newM.NomeTom};{newM.NomeIbge};{newM.Uf};{diffs}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignora */ }
        }
    }

    internal sealed class Municipio
    {
        public string? Tom { get; set; }
        public string? Ibge { get; set; }
        public string? NomeTom { get; set; }
        public string? NomeIbge { get; set; }
        public string? Uf { get; set; }

        public string NomePreferido =>
            !string.IsNullOrWhiteSpace(NomeIbge) ? NomeIbge! :
            !string.IsNullOrWhiteSpace(NomeTom) ? NomeTom! :
            "";

        public string ToConcatenatedString()
            => $"{Tom}|{Ibge}|{NomeTom}|{NomeIbge}|{Uf}";

        public static bool EqualsSemChave(Municipio a, Municipio b)
            => string.Equals(a.NomeTom, b.NomeTom, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.NomeIbge, b.NomeIbge, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Uf, b.Uf, StringComparison.OrdinalIgnoreCase);

        public string DiffCampos(Municipio other)
        {
            var sb = new StringBuilder();
            if (!string.Equals(NomeTom, other.NomeTom, StringComparison.OrdinalIgnoreCase))
                sb.Append($"NomeTOM: '{NomeTom}' -> '{other.NomeTom}' | ");
            if (!string.Equals(NomeIbge, other.NomeIbge, StringComparison.OrdinalIgnoreCase))
                sb.Append($"NomeIBGE: '{NomeIbge}' -> '{other.NomeIbge}' | ");
            if (!string.Equals(Uf, other.Uf, StringComparison.OrdinalIgnoreCase))
                sb.Append($"UF: '{Uf}' -> '{other.Uf}' | ");
            return sb.ToString().TrimEnd(' ', '|');
        }
    }

    internal static class Util
    {
        public static string San(string? s) => (s ?? "").Trim();

        // Helpers de hashing (opcional)
        public static byte[] BuildSalt(string? ibge)
        {
            var baseSalt = "pepper-fixo";
            return SHA256.HashData(Encoding.UTF8.GetBytes($"{ibge}|{baseSalt}"));
        }

        public static string DeriveHashHex(string password, byte[] salt, int iterations = 50_000, int bytes = 32)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            return Convert.ToHexString(pb3df2: pbkdf2.GetBytes(bytes));
        }
    }
}
