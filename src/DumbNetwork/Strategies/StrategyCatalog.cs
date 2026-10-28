namespace DumbNetwork.Strategies;

/// <summary>
/// Ready-made strategies. Linux arguments were validated with
/// <c>nfqws --dry-run</c> against the bundled backend version.
/// </summary>
public static class StrategyCatalog
{
    private const string FakeTlsGoogle = "{fake:tls_clienthello_www_google_com.bin}";
    private const string FakeQuicGoogle = "{fake:quic_initial_www_google_com.bin}";
    private const string FakeHttpIana = "{fake:http_iana_org.bin}";
    private const string FakeStun = "{fake:stun.bin}";
    private const string FakeDiscord = "{fake:discord-ip-discovery-with-port.bin}";

    public static IReadOnlyList<StrategyDefinition> All { get; } =
    [
        new StrategyDefinition(
            Id: "fake-tls-quic",
            Name: "Fake TLS + QUIC",
            Description:
                "Подмена TLS ClientHello и QUIC Initial. Универсальный вариант, не зависит от списков доменов.",
            Badge: "TLS + QUIC",
            Arguments:
            [
                "--filter-tcp=80,443",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=ts",
                $"--dpi-desync-fake-tls={FakeTlsGoogle}",
                $"--dpi-desync-fake-http={FakeHttpIana}",
                "--new",
                "--filter-udp=443",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                $"--dpi-desync-fake-quic={FakeQuicGoogle}",
            ],
            TcpPorts: ["80", "443"],
            UdpPorts: ["443"]),

        new StrategyDefinition(
            Id: "multisplit-seqovl",
            Name: "Multisplit + SeqOvl",
            Description:
                "Разбиение TLS-запроса с перекрытием последовательности (seqovl). Хорошо работает на Windows и Linux.",
            Badge: "MULTISPLIT",
            Arguments:
            [
                "--filter-tcp=80,443",
                "--dpi-desync=multisplit",
                "--dpi-desync-split-pos=1",
                "--dpi-desync-split-seqovl=568",
                $"--dpi-desync-split-seqovl-pattern={FakeTlsGoogle}",
            ],
            TcpPorts: ["80", "443"],
            UdpPorts: []),

        new StrategyDefinition(
            Id: "classic-bolvan",
            Name: "Классика (bolvan)",
            Description:
                "Схема из официального config.default: fake + multisplit для HTTP и fake + multidisorder для TLS.",
            Badge: "CLASSIC",
            Arguments:
            [
                "--filter-tcp=80",
                "--dpi-desync=fake,multisplit",
                "--dpi-desync-split-pos=method+2",
                "--dpi-desync-fooling=md5sig",
                "--new",
                "--filter-tcp=443",
                "--dpi-desync=fake,multidisorder",
                "--dpi-desync-split-pos=1,midsld",
                "--dpi-desync-fooling=badseq,md5sig",
                "--new",
                "--filter-udp=443",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
            ],
            TcpPorts: ["80", "443"],
            UdpPorts: ["443"],
            Platforms: PlatformSupport.Linux),

        new StrategyDefinition(
            Id: "discord-stun",
            Name: "Discord + STUN",
            Description:
                "Подмена UDP-пакетов Discord и STUN на портах голосового трафика.",
            Badge: "DISCORD",
            Arguments:
            [
                "--filter-udp=19294-19344,50000-50100",
                "--filter-l7=discord,stun",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                $"--dpi-desync-fake-discord={FakeDiscord}",
                $"--dpi-desync-fake-stun={FakeStun}",
            ],
            TcpPorts: [],
            UdpPorts: ["19294-19344", "50000-50100"]),

        new StrategyDefinition(
            Id: "quic-only",
            Name: "Только QUIC",
            Description:
                "Только QUIC (UDP 443). Помогает, если браузер использует HTTP/3.",
            Badge: "QUIC",
            Arguments:
            [
                "--filter-udp=443",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                $"--dpi-desync-fake-quic={FakeQuicGoogle}",
            ],
            TcpPorts: [],
            UdpPorts: ["443"]),

        new StrategyDefinition(
            Id: "http-only",
            Name: "HTTP (80)",
            Description:
                "Обход для незашифрованного HTTP (TCP 80).",
            Badge: "HTTP",
            Arguments:
            [
                "--filter-tcp=80",
                "--dpi-desync=fake,multisplit",
                "--dpi-desync-split-pos=method+2",
                "--dpi-desync-repeats=4",
                $"--dpi-desync-fake-http={FakeHttpIana}",
            ],
            TcpPorts: ["80"],
            UdpPorts: []),

        new StrategyDefinition(
            Id: "aggressive",
            Name: "Агрессивная",
            Description:
                "Максимальное покрытие: HTTPS на альтернативных портах, QUIC и Discord. Больше нагрузки на CPU.",
            Badge: "MAX",
            Arguments:
            [
                "--filter-tcp=80,443,2053,2083,2087,2096,8443",
                "--dpi-desync=fake,multidisorder",
                "--dpi-desync-split-pos=1,midsld",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=ts",
                $"--dpi-desync-fake-tls={FakeTlsGoogle}",
                $"--dpi-desync-fake-http={FakeHttpIana}",
                "--new",
                "--filter-udp=443,19294-19344,50000-50100",
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                $"--dpi-desync-fake-quic={FakeQuicGoogle}",
                $"--dpi-desync-fake-discord={FakeDiscord}",
                $"--dpi-desync-fake-stun={FakeStun}",
            ],
            TcpPorts: ["80", "443", "2053", "2083", "2087", "2096", "8443"],
            UdpPorts: ["443", "19294-19344", "50000-50100"]),
    ];

    public static IReadOnlyList<StrategyDefinition> ForPlatform(bool isWindows)
    {
        return All
            .Where(strategy => strategy.Platforms == PlatformSupport.All
                || (isWindows && strategy.Platforms == PlatformSupport.Windows)
                || (!isWindows && strategy.Platforms == PlatformSupport.Linux))
            .ToArray();
    }
}
