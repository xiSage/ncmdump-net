namespace LibNCM;

public class NcmException : Exception
{
    public NcmException(string message) : base(message) { }
    public NcmException(string message, Exception innerException) : base(message, innerException) { }
}

public class NcmFileFormatException : NcmException
{
    public NcmFileFormatException(string message) : base(message) { }
    public NcmFileFormatException(string message, Exception innerException) : base(message, innerException) { }
}

public class NcmDecryptionException : NcmException
{
    public NcmDecryptionException(string message) : base(message) { }
    public NcmDecryptionException(string message, Exception innerException) : base(message, innerException) { }
}

public class NcmMetadataException : NcmException
{
    public NcmMetadataException(string message) : base(message) { }
    public NcmMetadataException(string message, Exception innerException) : base(message, innerException) { }
}