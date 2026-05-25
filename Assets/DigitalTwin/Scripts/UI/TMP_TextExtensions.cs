using TMPro;

namespace DigitalTwin
{
    public static class TMP_TextExtensions
    {
        public static bool SetIfChanged(this TMP_Text text, string value)
        {
            if (text == null)
            {
                return false;
            }

            if (value == null)
            {
                value = string.Empty;
            }
            if (text.text == value)
            {
                return false;
            }

            text.text = value;
            return true;
        }
    }
}
