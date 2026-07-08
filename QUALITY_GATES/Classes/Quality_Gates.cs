namespace QUALITY_GATES.Classes;

using QUALITY_GATES.Data;

public partial class Class_Projects_Quality_Gates
{
    private readonly IDbConnectionFactory _db;

    public Class_Projects_Quality_Gates(IDbConnectionFactory db)
    {
        _db = db;
    }
}
