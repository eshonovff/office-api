namespace Office.Api.Common;

public static class AgeCalculator
{
    /// <summary>Синну соли пурраи то `asOf` — рӯз/моҳи ҳанӯз нарасида ҳисоб намешавад.</summary>
    public static int Calculate(DateOnly birthDate, DateOnly asOf)
    {
        var age = asOf.Year - birthDate.Year;
        if (asOf < birthDate.AddYears(age))
            age--;

        return age;
    }
}
