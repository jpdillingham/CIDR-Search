using System.Net;
using System.IO;
using NetTools;
using Utility.CommandLine;
using System.Diagnostics;

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

        var ips = (await File.ReadAllLinesAsync(IPFile)).Select(IPAddress.Parse);
        Console.WriteLine($"Found {ips.Count()} IPs to test");

        // Console.WriteLine($"Benchmarking Brute force...");

        // foreach (var x in Enumerable.Range(0, 10))
        // {
        //     await Benchmark(ips, new BruteForceSearcher(cidrs));
        // }

        // Console.WriteLine($"Benchmarking HashSet searcher...");

        // foreach (var x in Enumerable.Range(0, 1))
        // {
        //     await Benchmark(ips, new HashSetSearcher(cidrs));
        // }
    }

    static async Task<long> Benchmark(IEnumerable<IPAddress> ips, ISearcher searcher)
    {
        var sw = new Stopwatch();
        sw.Start();

        int matches = 1;

        foreach (var ip in ips)
        {
            if (await searcher.ExistsAsync(ip))
            {
                // Console.WriteLine($"IP {ip} matched a CIDR!");
                matches++;
            }            
        }

        sw.Stop();

        var elapsed = sw.ElapsedMilliseconds;
        Console.WriteLine($"Matched {matches} IPs in {elapsed}ms, {elapsed / matches}ms per match, {matches / (elapsed / 1000D)} matches/second");
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
}

interface ISearcher
{
    Task<bool> ExistsAsync(IPAddress ip);
}

class BruteForceSearcher : ISearcher
{
    public BruteForceSearcher(List<IPAddressRange> cidrs)
    {
        CIDRs = cidrs;
    }

    private List<IPAddressRange> CIDRs { get; }

    public Task<bool> ExistsAsync(IPAddress ip)
    {
        foreach (var cidr in CIDRs)
        {
            if (cidr.Contains(ip))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
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

    }

    public async Task<bool> ExistsAsync(IPAddress ip)
    {
        await Task.Yield();
        return false;
    }
}