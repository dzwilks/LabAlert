using System.Globalization;
using System.Text.RegularExpressions;
using NHapi.Base.Model;
using NHapi.Base.Parser;
using NHapi.Base.Util;

namespace LabAlert;

public record LabResult(string PatientId, string PatientName, string Unit, string OrderId,
    string OrderName, string TestCode, string TestName, string Value, string Units,
    string ReferenceRange, string AbnormalFlag, string Severity);

public static partial class Hl7Results
{
    [GeneratedRegex(@"^\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*-\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*$")]
    private static partial Regex RangePattern();

    public static IReadOnlyList<LabResult> Parse(string text)
    {
        var message = new PipeParser().Parse(text);
        var msh = (ISegment)message.GetStructure("MSH");
        if (Field(msh, 9) != "ORU" || Field(msh, 9, 2) != "R01" || Field(msh, 10) == "")
            throw new InvalidDataException("Expected an ORU^R01 with a message control ID.");

        var results = new List<LabResult>();
        string patientId = "", patientName = "", unit = "", orderId = "", orderName = "";
        bool hasOrder = false;
        var encoding = EncodingCharacters.FromMessage(message);
        foreach (var (name, segment) in Segments(message))
        {
            if (segment is null)
            {
                if (name is "PATIENT_RESULT" or "RESPONSE")
                    patientId = patientName = unit = orderId = orderName = "";
                if (name is "PATIENT_RESULT" or "RESPONSE" or "ORDER_OBSERVATION")
                    hasOrder = false;
                continue;
            }
            switch (name)
            {
                case "PID":
                    patientId = Field(segment, 3);
                    patientName = string.Join(" ", new[] { Field(segment, 5, 2), Field(segment, 5) }.Where(s => s != ""));
                    unit = orderId = orderName = "";
                    hasOrder = false;
                    break;
                case "PV1":
                    unit = Field(segment, 3);
                    break;
                case "OBR":
                    orderId = Field(segment, 3);
                    if (orderId == "") orderId = Field(segment, 2);
                    orderName = Field(segment, 4, 2);
                    if (orderName == "") orderName = Field(segment, 4);
                    hasOrder = true;
                    break;
                case "OBX":
                    if (patientId == "" || !hasOrder)
                        throw new InvalidDataException("Every result must have a patient and order.");
                    var value = string.Join(encoding.RepetitionSeparator,
                        segment.GetField(5).Select(field => PipeParser.Encode(field, encoding)));
                    var range = Field(segment, 7);
                    var flags = string.Join("~", segment.GetField(8).Select((_, i) => Field(segment, 8, 1, i)).Where(s => s != ""));
                    results.Add(new LabResult(patientId, patientName, unit, orderId, orderName,
                        Field(segment, 3), Field(segment, 3, 2), value, Field(segment, 6), range, flags,
                        Severity(flags, value, range)));
                    break;
            }
        }
        if (results.Count == 0) throw new InvalidDataException("Message contains no results.");
        return results;
    }

    private static string Field(ISegment segment, int field, int component = 1, int repetition = 0) =>
        Terser.Get(segment, field, repetition, component, 1) ?? "";

    private static IEnumerable<(string Name, ISegment? Segment)> Segments(IGroup group)
    {
        foreach (var name in group.Names)
        foreach (var structure in group.GetAll(name))
        {
            if (structure is ISegment segment) yield return (name, segment);
            else if (structure is IGroup child)
            {
                yield return (name, null);
                foreach (var nested in Segments(child)) yield return nested;
            }
        }
    }

    private static string Severity(string flags, string value, string range)
    {
        var codes = flags.Split('~', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (codes.Any(c => c.Equals("AA", StringComparison.OrdinalIgnoreCase) ||
                           c.Equals("HH", StringComparison.OrdinalIgnoreCase) ||
                           c.Equals("LL", StringComparison.OrdinalIgnoreCase) ||
                           c.Equals("PANIC", StringComparison.OrdinalIgnoreCase)))
            return "Critical";
        if (codes.Any(c => !c.Equals("N", StringComparison.OrdinalIgnoreCase)))
            return "Abnormal";
        var match = RangePattern().Match(range);
        if (match.Success &&
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            decimal.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var low) &&
            decimal.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var high) &&
            low <= high && (number < low || number > high))
            return "Abnormal";
        return "Normal";
    }
}
