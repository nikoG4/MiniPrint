namespace MiniPrint.Protocol;

public enum IppOperation : ushort
{
    PrintJob = 0x0002,
    ValidateJob = 0x0004,
    CreateJob = 0x0005,
    SendDocument = 0x0006,
    CancelJob = 0x0008,
    GetJobAttributes = 0x0009,
    GetJobs = 0x000A,
    GetPrinterAttributes = 0x000B,
}

public enum IppStatus : ushort
{
    SuccessfulOk = 0x0000,
    SuccessfulOkIgnoredOrSubstitutedAttributes = 0x0001,
    ClientErrorBadRequest = 0x0400,
    ClientErrorNotPossible = 0x0404,
    ClientErrorNotFound = 0x0406,
    ClientErrorRequestEntityTooLarge = 0x0408,
    ClientErrorDocumentFormatNotSupported = 0x040A,
    ClientErrorAttributesOrValuesNotSupported = 0x040B,
    ServerErrorInternalError = 0x0500,
    ServerErrorOperationNotSupported = 0x0501,
    ServerErrorServiceUnavailable = 0x0502,
    ServerErrorVersionNotSupported = 0x0503,
    ServerErrorNotAcceptingJobs = 0x0506,
    ServerErrorMultipleDocumentJobsNotSupported = 0x0509,
}

public enum IppDelimiterTag : byte
{
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,
}

public enum IppValueTag : byte
{
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
}

public static class IppJobState
{
    public const int Pending = 3;
    public const int PendingHeld = 4;
    public const int Processing = 5;
    public const int Canceled = 7;
    public const int Aborted = 8;
    public const int Completed = 9;
}

public static class IppPrinterState
{
    public const int Idle = 3;
    public const int Processing = 4;
    public const int Stopped = 5;
}
