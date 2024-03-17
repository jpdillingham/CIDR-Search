# CIDR-Search
Benchmarking techniques for checking an IP address against a list of CIDRs

This is a research project to determine the best approach for IP filtering of messages for [slskd](https://github.com/slskd/slskd).

## Winning Approach

A variation of the 'Brute force' approach; creating a dictionary keyed by the first octet of the addresses spanned by each CIDR, and with a value containing a list of the CIDRs falling into that octet.

To test an IP address, take the first octet and find the corresponding list in the dictionary.  If there is no entry, the IP is not covered by a CIDR.  If there is an entry, iterate over the list of CIDRs and test the IP against each, returning upon the first hit.

This approach yields the following resuls when running the code in this repository:

```
Matched 16769 IPs in 634ms, 0ms per match, 26449.526813880126 matches/second
Average ns: 1611.7485687022902
Min ns: (28, 0)
Max ns: (147, 393500)
```

And here are the top 10 longest checks, by octet:

```
{"Octet":147,"Time":393500}
{"Octet":192,"Time":260000}
{"Octet":65,"Time":159600}
{"Octet":209,"Time":138000}
{"Octet":194,"Time":127700}
{"Octet":12,"Time":123000}
{"Octet":198,"Time":113300}
{"Octet":202,"Time":109100}
{"Octet":199,"Time":99900}
{"Octet":208,"Time":64800}
```

The code tested 100,000 random IP addresses against roughly 250,000 CIDRs in 634 milliseconds, averaging 1611 nanoseconds (0.001611 milliseconds) per match, with the longest test taking 393,500 nanoseconds (0.3935 milliseconds).

The memory footprint of the dictionary is roughly 30mb, which is (subjectively) quite a lot, but the results are worth the tradeoff.

All benchmarks were produced on an Intel i5-3570 @ 3.40ghz (stock) with 24gb RAM and an SSD.

### Additional benchmarks

Raspberry Pi 2:

```
Matched 16769 IPs in 10043ms, 0ms per match, 1669.720203126556 matches/second
Average ns: 27107.034947519085
Min ns: (21, 1770)
Max ns: (195, 3193442)
```

Raspberry Pi 3/armv7 (32 bit OS):

```
Matched 16769 IPs in 6668ms, 0ms per match, 2514.8470305938813 matches/second
Average ns: 18295.95079914122
Min ns: (11, 1458)
Max ns: (7, 1325767)

```

Raspberry Pi 3/arm64 (64 bit OS):

Quite interesting that the 64 bit is nearly twice as slow!

```
Matched 16769 IPs in 11555ms, 0ms per match, 1451.2332323669407 matches/second
Average ns: 30772.02331822519
Min ns: (29, 677)
Max ns: (7, 1580938)
```

Raspberry Pi 4:

```
Matched 16769 IPs in 1696ms, 0ms per match, 9887.382075471698 matches/second
Average ns: 4758.110567748092
Min ns: (33, 240)
Max ns: (194, 202496)
```

## Other Approaches

### Brute force

Add all of the CIDRs to a `List<IPAddressRange>` and iterate over it, stopping if the IP was matched.  This was used primarily to establish a baseline.

### HashSet

Add all of the IPs covered by the CIDRs to  `HashSet<uint>` and simply check for the existence of the tested IP, the idea being that access should be incredibly fast.

This was a bust; it wasn't really any faster than the the brute force approach and it ran my PC out of memory.  I had to temporarily reduce the CIDR list to about 10k records (from ~250k) to get it to work.

### SQLite

Create an in-memory database and add the first and last IPs for each CIDR to a table, an test IPs by way of `SELECT * FROM cidrs WHERE ip BETWEEN first AND last`

This was surprisingly slow, roughly the same as the brute force method.

### SQLite with `rtree`

Create an in-memory database using the `rtree` extension and test IPs `WHERE start <= ip AND end >= ip`

This was incredibly fast, but it produced false positives for IPs that were _close_ to a CIDR but not within.  When results would come back the `start` and `end` fields would span multiple records.

The false positives are likely just me not really understanding the `rtree` extension, or possibly a loss of precision due to the rtree implementation using floating point numbers.  I tried `rtree_int32` and it produced similar (incorrect) results, surely because the IP data is represented as an unsigned integer.

If I wanted to experiment further I would remove the first octet of the IP address and create 255 tables, one for each octet, to prove (or disprove) the loss of precision theory.