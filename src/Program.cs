using System.Net;
using System.IO;
using NetTools;
using Utility.CommandLine;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Collections.Concurrent;

class Program
{
    [Argument('f', "file")]
    private static string InputFile { get; set; } = "../data/cidrs.txt";

    [Argument('i', "ips")]
    private static string IPFile { get; set; } = "../data/ips.txt";

    static async Task Main(string[] args)
    {
        Arguments.Populate(clearExistingValues: false);

        Console.WriteLine($"Grabbing CIDRs from file {InputFile}...");

        var contents = await File.ReadAllLinesAsync(InputFile);
        var cidrs = new List<IPAddressRange>();

        foreach (var line in contents)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith('#'))
            {
                continue;
            }

            if (IPAddressRange.TryParse(line, out var cidr))
            {
                cidrs.Add(cidr);
            }
            else
            {
                Console.WriteLine($"Invalid CIDR: {line}");
            }
        }

        Console.WriteLine($"Found {cidrs.Count()} CIDRs");
        cidrs = cidrs.Distinct().ToList();
        Console.WriteLine($"Deduped to {cidrs.Count()} CIDRs");

        var ips = (await File.ReadAllLinesAsync(IPFile)).Select(IPAddress.Parse);
        Console.WriteLine($"Found {ips.Count()} IPs to test");

        Console.WriteLine($"Benchmarking Brute force...");

        var brute = new BruteForceSearcher(cidrs);
        foreach (var x in Enumerable.Range(0, 1))
        {
            await Benchmark(ips, brute);
        }

        // Console.WriteLine($"Benchmarking HashSet searcher...");

        // foreach (var x in Enumerable.Range(0, 1))
        // {
        //     await Benchmark(ips, new HashSetSearcher(cidrs));
        // }

        // Console.WriteLine($"Benchmarking Sqlite searcher...");

        // var sqlite = new SQLiteSearcher(cidrs);

        // foreach (var x in Enumerable.Range(0, 1))
        // {
        //     await Benchmark(ips, sqlite);
        // }

        // Console.WriteLine($"Benchmarking Sqlite Range searcher...");
        // var sqliteRange = new SQLiteRangeSearcher(cidrs);

        // foreach (var x in Enumerable.Range(0, 1))
        // {
        //     await Benchmark(ips, sqliteRange);
        // }
    }

    static async Task<long> Benchmark(IEnumerable<IPAddress> ips, ISearcher searcher)
    {
        var sw = new Stopwatch();
        sw.Start();

        int matches = 0;

        foreach (var ip in ips)
        {
            if (await searcher.ExistsAsync(ip))
            {
                //Console.WriteLine(ip);
                matches++;
            }            
        }

        sw.Stop();

        var elapsed = sw.ElapsedMilliseconds;

        if (matches > 0) 
        {
            Console.WriteLine($"Matched {matches} IPs in {elapsed}ms, {elapsed / matches}ms per match, {matches / (elapsed / 1000D)} matches/second");
        }
        else
        {
            Console.WriteLine("No matches");
        }

        return sw.ElapsedMilliseconds;
    }
}

static class Shared 
{
    public static uint ToUint32(this IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    public static IPAddress NextIP(this IPAddress ip, int count)
    {
        var ipAsUint = ip.ToUint32();
        var nextAsUint = ipAsUint + count;
        var nextAsBytes = BitConverter.GetBytes(nextAsUint);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(nextAsBytes);
        }

        nextAsUint = BitConverter.ToUInt32(nextAsBytes, 0);
        return new IPAddress(nextAsUint);
    }
    public static int ExecuteNonQuery(this SqliteConnection conn, string query, Action<SqliteCommand> action = null)
    {
        using var cmd = new SqliteCommand(query, conn);
        action?.Invoke(cmd);
        return cmd.ExecuteNonQuery();
    }
}

interface ISearcher
{
    Task<bool> ExistsAsync(IPAddress ip);
}

class BruteForceSearcher : ISearcher
{
    public BruteForceSearcher(List<IPAddressRange> cidrs)
    {
        var sw = new Stopwatch();
        sw.Start();

        foreach (var cidr in cidrs)
        {
            var first = int.Parse(cidr.Begin.ToString().Split('.')[0]);
            var last = int.Parse(cidr.End.ToString().Split('.')[0]);

            var entry = (cidr.Begin.ToUint32(), cidr.End.ToUint32());

            // CIDRs with a mask of /7 and lower span multiple octets, so we must add these CIDRs to the list for each octet
            for (int i = 0; i <= last - first; i++)
            {
                CIDRs.AddOrUpdate(
                    key: first + i,
                    addValueFactory: (_) => new List<(uint, uint)> { entry },
                    updateValueFactory: (_, list) =>
                    {
                        list.Add(entry);
                        return list;
                    }
                );
            }
        }

        sw.Stop();
        Console.WriteLine($"Populated dictionary in {sw.ElapsedMilliseconds}ms");
    }

    private ConcurrentDictionary<int, List<(uint, uint)>> CIDRs { get; } = new ConcurrentDictionary<int, List<(uint, uint)>>();


    public async Task<bool> ExistsAsync(IPAddress ip)
    {
        await Task.Yield();

        var first = int.Parse(ip.ToString().Split('.')[0]);

        if (!CIDRs.TryGetValue(first, out List<(uint, uint)>? value)) return false;

        var ipAsUint32 = ip.ToUint32();

        foreach (var cidr in value)
        {
            if (ipAsUint32 >= cidr.Item1 && ipAsUint32 <= cidr.Item2)
            {
                return true;
            }
        }

        return false;
    }
}


class HashSetSearcher : ISearcher
{
    public HashSetSearcher(List<IPAddressRange> cidrs)
    {
        var sw = new Stopwatch();
        sw.Start();

        foreach (var cidr in cidrs)
        {
            Console.WriteLine($"Range {cidr}");

            var first = cidr.Begin.ToUint32();
            var last = cidr.End.ToUint32();

            for (uint i = 0; i <= last - first; i++)
            {
                var next = first + i;

                var rev = BitConverter.GetBytes(next);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(rev);
                }

                var ok = BitConverter.ToUInt32(rev, 0);

                //Console.WriteLine(new IPAddress(ok));
                HashSet.Add(next);
            }

            //break;
        }

        sw.Stop();
        Console.WriteLine($"Initialized HashSet with {HashSet.Count} records in {sw.ElapsedMilliseconds}ms");
    }

    private HashSet<uint> HashSet { get; } = new HashSet<uint>();

    public async Task<bool> ExistsAsync(IPAddress ip)
    {
        await Task.Yield();
        return false;
    }
}

class SQLiteSearcher : ISearcher
{
    public SQLiteSearcher(List<IPAddressRange> cidrs)
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        Console.WriteLine("Creating table...");

        using var create = Connection.CreateCommand();
        create.CommandText = "CREATE TABLE cidrs (start INTEGER, end INTEGER); CREATE INDEX idx_start ON cidrs (start); CREATE INDEX idx_end ON cidrs (end);";
        create.ExecuteNonQuery();

        var sw = new Stopwatch();
        sw.Start();

        Console.WriteLine("Table created.  Filling...");

        foreach (var cidr in cidrs)
        {
            Connection.ExecuteNonQuery("INSERT INTO cidrs (start, end) VALUES(@start, @end)", cmd => {
                cmd.Parameters.AddWithValue("start", cidr.Begin.ToUint32());
                cmd.Parameters.AddWithValue("end", cidr.End.ToUint32());
            });
        }

        sw.Stop();
        Console.WriteLine($"Table filled in {sw.ElapsedMilliseconds}ms");
    }

    private SqliteConnection Connection { get; }

    public async Task<bool> ExistsAsync(IPAddress ip)
    {
        await Task.Yield();

        using var cmd = new SqliteCommand("SELECT start, end FROM cidrs WHERE @ip BETWEEN start AND end LIMIT 1", Connection);
        cmd.Parameters.AddWithValue("ip", ip.ToUint32());

        var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return false;
        }

        return true;
    }
}

class SQLiteRangeSearcher : ISearcher
{
    // https://www.sqlite.org/rtree.html
    public SQLiteRangeSearcher(List<IPAddressRange> cidrs)
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        Console.WriteLine("Creating table...");

        using var create = Connection.CreateCommand();
        create.CommandText = "CREATE VIRTUAL TABLE cidr_lookup USING rtree(idx, start, end); CREATE TABLE cidrs (idx, cidr);";
        create.ExecuteNonQuery();

        var sw = new Stopwatch();
        sw.Start();

        Console.WriteLine("Table created.  Filling...");

        foreach (var record in cidrs.Select((value, idx) => new { idx, value }))
        {
            // if (record.idx == 213120)
            // {
            //     Console.WriteLine(record.value.ToString());
            //     Console.WriteLine(record.value.ToCidrString());
            //     Console.WriteLine($"first: {record.value.Begin}-{record.value.End}");
            //     Console.WriteLine($"first: {record.value.Begin.ToUint32()}-{record.value.End.ToUint32()}");
            // }
            //Console.WriteLine($"INSERT: id:{record.value.ToCidrString()} start:{record.value.Begin.ToUint32()} end:{record.value.End.ToUint32()}");
            Connection.ExecuteNonQuery(
            @"
                INSERT INTO cidr_lookup (idx, start, end) VALUES(@idx, @start, @end);
                INSERT INTO cidrs (idx, cidr) VALUES(@idx, @cidr);
            ", cmd => {
                cmd.Parameters.AddWithValue("idx", record.idx);
                cmd.Parameters.AddWithValue("cidr", record.value.ToCidrString());
                cmd.Parameters.AddWithValue("start", record.value.Begin.ToUint32());
                cmd.Parameters.AddWithValue("end", record.value.End.ToUint32());
            });            
        }

        sw.Stop();
        Console.WriteLine($"Table filled in {sw.ElapsedMilliseconds}ms");
    }

    private SqliteConnection Connection { get; }

    public async Task<bool> ExistsAsync(IPAddress ip)
    {
        await Task.Yield();

        using var cmd = new SqliteCommand("SELECT idx, start, end FROM cidr_lookup WHERE start <= @ip AND end >= @ip LIMIT 1", Connection);
        cmd.Parameters.AddWithValue("ip", ip.ToUint32());

        var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return false;
        }

        var idx = reader.GetInt64(0);
        var start = reader.GetInt64(1);
        var end = reader.GetInt64(2);

        // // produces false positive.  rounding error?
        // if (ip.Equals(IPAddress.Parse("209.237.212.62")))
        // {
        //     Console.WriteLine($"id {id}");
        //     Console.WriteLine($"{ip} or {ip.ToUint32()} in {start}-{end}");
        //     Console.WriteLine(ip);
        // }

        using var cmd2 = new SqliteCommand("SELECT cidr FROM cidrs WHERE idx = @idx", Connection);
        cmd2.Parameters.AddWithValue("idx", idx);

        reader = cmd2.ExecuteReader();
        reader.Read();

        var cidr = reader.GetString(0);

        var range = IPAddressRange.Parse(cidr);
        var pass = range.Contains(ip) ? "PASS" : "FAIL";

        Console.WriteLine($"[{pass}] {ip} - {range.ToCidrString()}");

        return true;
    }
}