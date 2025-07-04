namespace Rubedo.Compiler.ContentBuilders
{
    public static class ErrorCodes
    {
        public const int NONE = 0;
        public const int SKIPPED = 1;
        internal const int END_OF_NON_ERRORS = 2;

        public const int BAD_JSON = 3;
        public const int MULTIPLE_MAKEATLAS = 4;
        public const int SUBDIRECTORY_MAKEATLAS = 5;
        public const int MISSING_FILE = 6;
    }
}