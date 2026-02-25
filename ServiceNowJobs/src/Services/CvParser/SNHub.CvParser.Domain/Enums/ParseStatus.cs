namespace SNHub.CvParser.Domain.Enums;

public enum ParseStatus
{
    Pending    = 1,
    Processing = 2,
    Completed  = 3,
    Failed     = 4
}

public enum CvContentType
{
    Pdf  = 1,
    Docx = 2
}
