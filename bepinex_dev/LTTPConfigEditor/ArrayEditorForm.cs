﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LTTPConfigEditor
{
    public partial class ArrayEditorForm : Form
    {
        public Type ArrayType { get; }
        public object ArrayObject { get; private set; }

        private Array array
        {
            get { return ArrayObject as Array; }
            set { ArrayObject = value; }
        }

        public ArrayEditorForm(Type _arrayType, object _arrayObj)
        {
            InitializeComponent();
            this.DialogResult = DialogResult.Cancel;

            this.ArrayType = _arrayType;
            this.ArrayObject = _arrayObj;

            buildDataGridView();
            buildChart();
        }

        public int GetJaggedDimensions()
        {
            return ArrayType.Name.Count((c) => c == '[');
        }

        private void buildDataGridView()
        {
            int[] indices = new int[array.Rank];
            for (int d = 0; d < array.Rank; d++)
            {
                indices[d] = 0;
                object val = array.GetValue(indices);

                int cols = 1;
                if (val.GetType().IsArray)
                {
                    Array innerArray = val as Array;
                    cols = innerArray.GetLength(0);
                }

                while (arrayDataGridView.Columns.Count < cols)
                {
                    DataGridViewTextBoxColumn newCol = new DataGridViewTextBoxColumn();
                    arrayDataGridView.Columns.Add(newCol);
                }
            }

            for (int d = 0; d < array.Rank; d++)
            {
                int rows = array.GetLength(d);
                for (int r = 0; r < rows; r++)
                {
                    DataGridViewRow row = new DataGridViewRow();
                    for (int c = 0; c < arrayDataGridView.Columns.Count; c++)
                    {
                        DataGridViewTextBoxCell cell = new DataGridViewTextBoxCell();
                        row.Cells.Add(cell);
                    }
                    arrayDataGridView.Rows.Add(row);

                    indices[d] = r;
                    object val = array.GetValue(indices);

                    if (val.GetType().IsArray)
                    {
                        Array innerArray = val as Array;
                        int cols = innerArray.GetLength(0);
                        for (int c = 0; c < cols; c++)
                        {
                            arrayDataGridView.Rows[r].Cells[c].Value = innerArray.GetValue(c);
                        }
                    }
                    else
                    {
                        arrayDataGridView.Rows[r].Cells[0].Value = val;
                    }
                }
            }
        }

        private void buildChart()
        {
            arrayChart.Series[0].Points.Clear();

            int rows = arrayDataGridView.Rows.Count - 1;
            int cols = arrayDataGridView.Columns.Count;
            switch (cols)
            {
                case 1:
                    for (int r = 0; r < rows; r++)
                    {
                        arrayChart.Series[0].Points.AddY(arrayDataGridView.Rows[r].Cells[0].Value);
                    }
                    break;
                case 2:
                    for (int r = 0; r < rows; r++)
                    {
                        arrayChart.Series[0].Points.AddXY(arrayDataGridView.Rows[r].Cells[0].Value, arrayDataGridView.Rows[r].Cells[1].Value);
                    }
                    break;
            }

        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void arrayDataGridViewCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            Type baseType = ArrayType;
            while (baseType.Name.Contains("[]"))
            {
                baseType = baseType.GetElementType();
            }

            try
            {
                object newValueObj = Convert.ChangeType(e.FormattedValue, baseType);
            }
            catch (FormatException)
            {
                e.Cancel = true;
                MessageBox.Show("Invalid entry. The value must be a " + baseType.Name + ".", "Invalid Config Change", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void arrayDataGridViewCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            buildChart();
        }
    }
}
