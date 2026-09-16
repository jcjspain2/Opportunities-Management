using GrupoPremo.Intranet.Library.IService;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static QUALITY_GATES.Data.SqlConnectionFactory;

public partial class Class_Projects_Quality_Gates
{
   // public async Task<OperationResult<List<MODEL_FORM_NEXT_GATE_ALLOCATION>>> Get_Next_Gate_Model(string OppLineId, string Gate_Id)
   // {


    //}

    /// <summary>
    /// Model for managing Form_Next_Gate, target is allocate users for next gate deliverables.
    /// </summary>
    public class MODEL_FORM_NEXT_GATE_ALLOCATION
    {
        public string OppLineId { get; set; } = string.Empty;
        public string Gate_Id { get; set; } = string.Empty;
        public string StatusiD { get; set; } = string.Empty;
        public int DeliverableID { get; set; } = 0;

        public string Deliverable_Name { get; set; } = string.Empty;
        public string Responsible_User_Id { get; set; } = string.Empty;
        public string Accountable_User_Id { get; set; } = string.Empty;


    }
}
