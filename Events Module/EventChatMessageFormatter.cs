using System;
using System.Text;

namespace Events_Module {

    internal enum EventChatMessageFormatFailure {
        None,
        EmptyFormat,
        MissingPoint,
        UnknownField,
        UnbalancedBraces
    }

    internal sealed class EventChatMessageValues {

        public string Point      { get; set; }
        public string EventZh    { get; set; }
        public string EventEn    { get; set; }
        public string CategoryZh { get; set; }
        public string CategoryEn { get; set; }
        public string Time       { get; set; }

    }

    internal sealed class EventChatMessageFormatResult {

        private EventChatMessageFormatResult(bool isValid,
                                             string text,
                                             EventChatMessageFormatFailure failure,
                                             string failureDetail) {
            IsValid      = isValid;
            Text         = text ?? string.Empty;
            Failure      = failure;
            FailureDetail = failureDetail ?? string.Empty;
        }

        public bool IsValid { get; }
        public string Text { get; }
        public EventChatMessageFormatFailure Failure { get; }
        public string FailureDetail { get; }

        internal static EventChatMessageFormatResult Success(string text) {
            return new EventChatMessageFormatResult(true, text, EventChatMessageFormatFailure.None, string.Empty);
        }

        internal static EventChatMessageFormatResult Failed(EventChatMessageFormatFailure failure,
                                                            string failureDetail = null) {
            return new EventChatMessageFormatResult(false, string.Empty, failure, failureDetail);
        }

    }

    internal sealed class EventClipboardTextResult {

        internal EventClipboardTextResult(string text,
                                          bool usedCustomFormat,
                                          bool fellBackToPoint,
                                          EventChatMessageFormatResult formatResult) {
            Text             = text ?? string.Empty;
            UsedCustomFormat = usedCustomFormat;
            FellBackToPoint  = fellBackToPoint;
            FormatResult     = formatResult;
        }

        public string Text { get; }
        public bool UsedCustomFormat { get; }
        public bool FellBackToPoint { get; }
        public EventChatMessageFormatResult FormatResult { get; }

    }

    internal static class EventChatMessageFormatter {

        internal static EventClipboardTextResult BuildClipboardText(bool useCustomFormat,
                                                                    string format,
                                                                    EventChatMessageValues values) {
            values = values ?? new EventChatMessageValues();
            string point = values.Point?.Trim() ?? string.Empty;

            if (!useCustomFormat) {
                return new EventClipboardTextResult(point, false, false, null);
            }

            EventChatMessageFormatResult formatResult = Format(format, values);
            return formatResult.IsValid
                ? new EventClipboardTextResult(formatResult.Text, true, false, formatResult)
                : new EventClipboardTextResult(point, false, true, formatResult);
        }

        internal static EventChatMessageFormatResult Format(string format, EventChatMessageValues values) {
            if (string.IsNullOrWhiteSpace(format)) {
                return EventChatMessageFormatResult.Failed(EventChatMessageFormatFailure.EmptyFormat);
            }

            values = values ?? new EventChatMessageValues();

            var output = new StringBuilder(format.Length + 64);
            bool includesPoint = false;

            for (int index = 0; index < format.Length;) {
                char current = format[index];

                if (current == '{') {
                    if (index + 1 < format.Length && format[index + 1] == '{') {
                        output.Append('{');
                        index += 2;
                        continue;
                    }

                    int closingBrace = format.IndexOf('}', index + 1);
                    if (closingBrace < 0 || format.IndexOf('{', index + 1, closingBrace - index - 1) >= 0) {
                        return EventChatMessageFormatResult.Failed(EventChatMessageFormatFailure.UnbalancedBraces);
                    }

                    string field = format.Substring(index + 1, closingBrace - index - 1);
                    if (!TryGetValue(field, values, out string replacement)) {
                        return EventChatMessageFormatResult.Failed(EventChatMessageFormatFailure.UnknownField, field);
                    }

                    if (string.Equals(field, "point", StringComparison.Ordinal)) {
                        includesPoint = true;
                    }

                    output.Append(replacement);
                    index = closingBrace + 1;
                    continue;
                }

                if (current == '}') {
                    if (index + 1 < format.Length && format[index + 1] == '}') {
                        output.Append('}');
                        index += 2;
                        continue;
                    }

                    return EventChatMessageFormatResult.Failed(EventChatMessageFormatFailure.UnbalancedBraces);
                }

                output.Append(current);
                index++;
            }

            if (!includesPoint) {
                return EventChatMessageFormatResult.Failed(EventChatMessageFormatFailure.MissingPoint);
            }

            return EventChatMessageFormatResult.Success(output.ToString());
        }

        internal static string CombineBilingual(string localized, string english) {
            string localizedValue = localized?.Trim() ?? string.Empty;
            string englishValue   = english?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(localizedValue)) return englishValue;
            if (string.IsNullOrEmpty(englishValue) ||
                string.Equals(localizedValue, englishValue, StringComparison.CurrentCultureIgnoreCase)) {
                return localizedValue;
            }

            return localizedValue + " / " + englishValue;
        }

        private static bool TryGetValue(string field, EventChatMessageValues values, out string value) {
            switch (field) {
                case "point":
                    value = values.Point ?? string.Empty;
                    return true;
                case "event":
                    value = CombineBilingual(values.EventZh, values.EventEn);
                    return true;
                case "event_zh":
                    value = values.EventZh ?? string.Empty;
                    return true;
                case "event_en":
                    value = values.EventEn ?? string.Empty;
                    return true;
                case "category":
                    value = CombineBilingual(values.CategoryZh, values.CategoryEn);
                    return true;
                case "category_zh":
                    value = values.CategoryZh ?? string.Empty;
                    return true;
                case "category_en":
                    value = values.CategoryEn ?? string.Empty;
                    return true;
                case "time":
                    value = values.Time ?? string.Empty;
                    return true;
                default:
                    value = string.Empty;
                    return false;
            }
        }

    }

}
