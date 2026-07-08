using QUALITY_GATES;
using QUALITY_GATES.Classes;
using QUALITY_GATES.Models;
using static QUALITY_GATES.Data.SqlConnectionFactory;

namespace Tester_Quality_Gates;

public partial class Form1 : Form
{
    private readonly Class_Projects_Quality_Gates _projectos;
    const string _Module = "QGATES";

    public Form1(Class_Projects_Quality_Gates projectos)
    {
        InitializeComponent();
        _projectos = projectos;
    }

    private async void button1_Click(object sender, EventArgs e)
    {
       
    }

 

    private async void button2_Click(object sender, EventArgs e)
    {
       //var result= await _projectos.Generate_Default_Gate("FEAS");
        var result1 = await _projectos.Get_Projects_Details();
    }
}
