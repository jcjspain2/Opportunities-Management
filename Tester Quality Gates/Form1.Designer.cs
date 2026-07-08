namespace Tester_Quality_Gates;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        button1 = new Button();
        dataGridView1 = new DataGridView();
        lblEstado = new Label();
        button2 = new Button();
        ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
        SuspendLayout();
        // 
        // button1
        // 
        button1.Location = new Point(12, 12);
        button1.Name = "button1";
        button1.Size = new Size(130, 30);
        button1.TabIndex = 0;
        button1.Text = "Cargar Proyectos";
        button1.Click += button1_Click;
        // 
        // dataGridView1
        // 
        dataGridView1.AllowUserToAddRows = false;
        dataGridView1.AllowUserToDeleteRows = false;
        dataGridView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dataGridView1.Location = new Point(12, 55);
        dataGridView1.Name = "dataGridView1";
        dataGridView1.ReadOnly = true;
        dataGridView1.Size = new Size(776, 383);
        dataGridView1.TabIndex = 2;
        // 
        // lblEstado
        // 
        lblEstado.AutoSize = true;
        lblEstado.Location = new Point(160, 18);
        lblEstado.Name = "lblEstado";
        lblEstado.Size = new Size(0, 15);
        lblEstado.TabIndex = 1;
        // 
        // button2
        // 
        button2.Location = new Point(618, 26);
        button2.Name = "button2";
        button2.Size = new Size(75, 23);
        button2.TabIndex = 3;
        button2.Text = "button2";
        button2.UseVisualStyleBackColor = true;
        button2.Click += button2_Click;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Controls.Add(button2);
        Controls.Add(button1);
        Controls.Add(lblEstado);
        Controls.Add(dataGridView1);
        Name = "Form1";
        Text = "Quality Gates — Tester";
        ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private Button button1;
    private DataGridView dataGridView1;
    private Label lblEstado;
    private Button button2;
}
