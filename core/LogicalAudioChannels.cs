namespace Core
{
    /// <summary>
    /// Canonical logical audio channel identifiers for VidiComm.
    /// These identifiers are mixer-agnostic and used across UI, PWA, and AudioControlService.
    /// AudioRoutingMap translates these logical IDs to device-specific physical channels.
    /// </summary>
    public static class LogicalAudioChannels
    {
        // Cameras 1 to 40
        public const long Camera1 = 1;
        public const long Camera2 = 2;
        public const long Camera3 = 3;
        public const long Camera4 = 4;
        public static long Camera(int index) => index;

        // Master Output Fader (Canonical Logical Master ID)
        public const long Master = 100;

        // Media Players 1 to 4
        public const long Media1 = 201;
        public const long Media2 = 202;
        public const long Media3 = 203;
        public const long Media4 = 204;
        public static long Media(int index) => 200 + index;

        // XLR / Analog Audio Inputs 1 and 2
        public const long Xlr1 = 301;
        public const long Xlr2 = 302;
        public static long Xlr(int index) => 300 + index;

        // MADI Digital Audio Inputs 1 to 32
        public const long Madi1 = 401;
        public const long Madi2 = 402;
        public static long Madi(int index) => 400 + index;

        public static bool IsCamera(long logicalChannel) => logicalChannel >= 1 && logicalChannel <= 40;
        public static bool IsMedia(long logicalChannel) => logicalChannel >= 201 && logicalChannel <= 204;
        public static bool IsXlr(long logicalChannel) => logicalChannel >= 301 && logicalChannel <= 302;
        public static bool IsMadi(long logicalChannel) => logicalChannel >= 401 && logicalChannel <= 432;
        public static bool IsMaster(long logicalChannel) => logicalChannel == Master;
    }

    /// <summary>
    /// Physical hardware audio identifiers specific to Blackmagic ATEM / Fairlight switchers.
    /// Used ONLY within AtemAudioDriver, AtemAudioMonitor, AtemHardwareAdapter, and ATEM routing configuration.
    /// </summary>
    public static class AtemPhysicalAudioChannels
    {
        // Cameras 1 to 40
        public static long Camera(int index) => index;

        // Master Output representation in ATEM Adapter/Monitor
        public const long MasterOut = 100;

        // Media Players 1 to 4
        public const long Media1 = 2001;
        public const long Media2 = 2002;
        public const long Media3 = 2003;
        public const long Media4 = 2004;
        public static long Media(int index) => 2000 + index;

        // XLR / Analog Audio Inputs 1 and 2
        public const long Xlr1 = 1301;
        public const long Xlr2 = 1401;
        public static long Xlr(int index) => index == 1 ? Xlr1 : (index == 2 ? Xlr2 : 1300 + index);

        // MADI Digital Audio Inputs 1 to 32
        public const long Madi1 = 1501;
        public static long Madi(int index) => 1500 + index;
    }
}
