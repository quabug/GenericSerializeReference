using System;

namespace GenericSerializeReference
{
    public enum LogLevel
    {
        Debug, Info, Warning, Error
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public class GenericSerializeReferenceLoggerAttribute : Attribute
    {
        public LogLevel LogLevel;
        public GenericSerializeReferenceLoggerAttribute(LogLevel logLevel) => LogLevel = logLevel;
    }
}