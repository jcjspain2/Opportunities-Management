using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QUALITY_GATES.Tools
{
    internal class Static_Local_Functions
    {
        public static string GetString(DataRow row, string column)
        {
            return row[column] == DBNull.Value || row[column] == null
                ? string.Empty
                : row[column].ToString()!;
        }

        public static int GetInt(DataRow row, string column)
        {
            return row[column] == DBNull.Value || row[column] == null
                ? 0
                : Convert.ToInt32(row[column]);
        }
        public static double GetDouble(DataRow row, string column)
        {
            return row[column] == DBNull.Value || row[column] == null
                ? 0
                : Convert.ToDouble(row[column]);
        }

        public static DateOnly GetDateOnly(DataRow row, string column)
        {
            return row[column] == DBNull.Value || row[column] == null
                ? DateOnly.MinValue
                : DateOnly.FromDateTime(Convert.ToDateTime(row[column]));
        }
    }
}
