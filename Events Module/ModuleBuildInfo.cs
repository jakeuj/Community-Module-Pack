namespace Events_Module {
    internal static class ModuleBuildInfo {
#if RELEASE_BUILD
        public static readonly bool SelfUpdateEnabled = true;
#else
        public static readonly bool SelfUpdateEnabled = false;
#endif
    }
}
