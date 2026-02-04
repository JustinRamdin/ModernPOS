namespace Pos.Application.Measurements;

public sealed record FeetInches(int Feet, int Inches);

public static class LengthConverter
{
    public const int InchesPerFoot = 12;

    public static int ToTotalInches(int feet, int inches)
    {
        if (feet < 0) throw new ArgumentOutOfRangeException(nameof(feet));
        if (inches < 0) throw new ArgumentOutOfRangeException(nameof(inches));
        return checked(feet * InchesPerFoot + inches);
    }

    public static FeetInches FromTotalInches(int totalInches)
    {
        if (totalInches < 0) throw new ArgumentOutOfRangeException(nameof(totalInches));
        var feet = totalInches / InchesPerFoot;
        var inches = totalInches % InchesPerFoot;
        return new FeetInches(feet, inches);
    }

    public static FeetInches Normalize(int feet, int inches)
    {
        if (feet < 0) throw new ArgumentOutOfRangeException(nameof(feet));
        if (inches < 0) throw new ArgumentOutOfRangeException(nameof(inches));

        var extraFeet = inches / InchesPerFoot;
        return new FeetInches(feet + extraFeet, inches % InchesPerFoot);
    }
}
