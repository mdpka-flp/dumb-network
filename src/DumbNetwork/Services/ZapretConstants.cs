namespace DumbNetwork.Services;

public static class ZapretConstants
{
    /// <summary>NFQUEUE number used by nfqws and the firewall rules.</summary>
    public const int QueueNumber = 200;

    /// <summary>Mark set by nfqws on generated packets to keep them out of NFQUEUE.</summary>
    public const string FwMark = "0x40000000";

    /// <summary>Dedicated nftables table. Safe to delete as a whole on stop.</summary>
    public const string NftTable = "dumbzapret";

    /// <summary>Dedicated iptables chain used when nft is unavailable.</summary>
    public const string IptablesChain = "DUMBZAPRET";
}
