﻿using Newtonsoft.Json;
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace LTTPConfigEditor
{
    public partial class LTTPConfigEditorForm : Form
    {
        private LateToTheParty.Configuration.ModConfig modConfig;
        private Configuration.ModPackageConfig modPackage;
        private Dictionary<string, Configuration.ConfigEditorInfoConfig> configEditorInfo;

        private BreadCrumbControl breadCrumbControl;
        private Dictionary<TreeNode, Type> configTypes = new Dictionary<TreeNode, Type>();
        private Dictionary<string, Action> valueButtonActions = new Dictionary<string, Action>();

        private bool isClosing = false;

        public LTTPConfigEditorForm()
        {
            InitializeComponent();

            breadCrumbControl = new BreadCrumbControl();
            breadCrumbControl.Dock = DockStyle.Fill;
            nodePropsTableLayoutPanel.Controls.Add(breadCrumbControl, 0, 0);
        }

        private void openToolStripButton_Click(object sender, EventArgs e)
        {
            if (openConfigDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            try
            {
                string packagePath = openConfigDialog.FileName.Substring(0, openConfigDialog.FileName.LastIndexOf('\\')) + "\\..\\package.json";
                modPackage = LoadConfig<Configuration.ModPackageConfig>(packagePath);

                if (!IsModVersionCompatible(new Version(modPackage.Version)))
                {
                    throw new InvalidOperationException("The selected configuration file is for a version of the LTTP mod that is incompatible with this version of the editor.");
                }

                modConfig = LoadConfig<LateToTheParty.Configuration.ModConfig>(openConfigDialog.FileName);
                configTypes.Clear();
                configTreeView.Nodes.AddRange(CreateTreeNodesForType(modConfig.GetType(), modConfig));

                string configEditorInfoFilename = openConfigDialog.FileName.Substring(0, openConfigDialog.FileName.LastIndexOf('\\')) + "\\configEditorInfo.json";
                configEditorInfo = LoadConfig<Dictionary<string, Configuration.ConfigEditorInfoConfig>>(configEditorInfoFilename);

                saveToolStripButton.Enabled = true;
                openToolStripButton.Enabled = false;
                loadTemplateButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error when Reading Configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void saveToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                SaveConfig(openConfigDialog.FileName, modConfig);
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Error when Saving Configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void loadTemplateButton_Click(object sender, EventArgs e)
        {

        }

        private void LTTPConfigEditorFormClosing(object sender, FormClosingEventArgs e)
        {
            if (MessageBox.Show("You have unsaved changes. Are you sure you want to quit?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.No)
            {
                e.Cancel = true;
            }

            isClosing = true;
        }

        private void ConfigNodeSelected(object sender, TreeViewEventArgs e)
        {
            Action callbackAction = new Action(() => {
                ConfigNodeSelected(configTreeView, new TreeViewEventArgs(configTreeView.SelectedNode));
            });

            BreadCrumbControl.UpdateBreadCrumbControlForTreeView(breadCrumbControl, configTreeView, e.Node, callbackAction);

            string configPath = GetConfigPathForTreeNode(e.Node);
            Configuration.ConfigEditorInfoConfig nodeConfigInfo = GetConfigInfoForPath(configPath);
            descriptionTextBox.Text = nodeConfigInfo.Description;

            object obj = GetObjectForConfigPath(modConfig, configPath);
            CreateValueControls(valueFlowLayoutPanel, obj, configTypes[e.Node], nodeConfigInfo);
        }

        private T LoadConfig<T>(string filename)
        {
            string json = File.ReadAllText(filename);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }

        private void SaveConfig<T>(string filename, T obj)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filename, json);
        }

        private bool IsModVersionCompatible(Version modVersion)
        {
            if (modVersion.CompareTo(LateToTheParty.Controllers.ConfigController.MinCompatibleModVersion) < 0)
            {
                return false;
            }

            if (modVersion.CompareTo(LateToTheParty.Controllers.ConfigController.MaxCompatibleModVersion) < 0)
            {
                return false;
            }

            return true;
        }

        private TreeNode[] CreateTreeNodesForType(Type type, object obj)
        {
            List<TreeNode> nodes = new List<TreeNode>();

            PropertyInfo[] props = type.GetProperties();
            foreach (PropertyInfo prop in props)
            {
                JsonPropertyAttribute jsonPropertyAttribute = prop.GetCustomAttribute<JsonPropertyAttribute>();
                string nodeName = jsonPropertyAttribute == null ? prop.Name : jsonPropertyAttribute.PropertyName;
                TreeNode node = new TreeNode(nodeName);
                Type propType = prop.PropertyType;

                if
                (
                    !propType.IsArray
                    && (propType != typeof(string))
                    && !(propType.IsGenericType && (propType.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                )
                {
                    node.Nodes.AddRange(CreateTreeNodesForType(propType, prop.GetValue(obj, null)));
                }

                if (propType.IsGenericType && (propType.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                {
                    IDictionary dict = prop.GetValue(obj, null) as IDictionary;
                    Type valueType = propType.GetGenericArguments()[1];
                    foreach(DictionaryEntry entry in dict)
                    {
                        TreeNode entryNode = new TreeNode(entry.Key.ToString());
                        node.Nodes.Add(entryNode);

                        Type keyType = propType.GetGenericArguments()[0];
                        var dictKey = typeof(ConfigDictionaryEntry<,>).MakeGenericType(keyType, valueType);
                        configTypes.Add(entryNode, dictKey);

                        entryNode.Nodes.AddRange(CreateTreeNodesForType(entry.Value.GetType(), entry.Value));
                    }
                }

                nodes.Add(node);
                configTypes.Add(node, propType);
            }

            return nodes.ToArray();
        }

        private Configuration.ConfigEditorInfoConfig GetConfigInfoForPath(string configPath)
        {
            if (configEditorInfo.ContainsKey(configPath))
            {
                return configEditorInfo[configPath];
            }

            return new Configuration.ConfigEditorInfoConfig();
        }

        private Configuration.ConfigEditorInfoConfig GetConfigInfoForTreeNode(TreeNode node)
        {
            return GetConfigInfoForPath(GetConfigPathForTreeNode(node));
        }

        private static string GetConfigPathForTreeNode(TreeNode node)
        {
            List<string> nodeNames = new List<string>();
            TreeNode currentNode = node;
            while (currentNode != null)
            {
                nodeNames.Add(currentNode.Text);
                currentNode = currentNode.Parent;
            }
            nodeNames.Reverse();

            return string.Join(".", nodeNames);
        }

        private object GetObjectForConfigPath(object obj, string configPath)
        {
            string[] pathElements = configPath.Split('.');

            PropertyInfo[] props = obj.GetType().GetProperties();
            foreach (PropertyInfo prop in props)
            {
                Type propType = prop.PropertyType;
                JsonPropertyAttribute jsonPropertyAttribute = prop.GetCustomAttribute<JsonPropertyAttribute>();
                string nodeName = jsonPropertyAttribute == null ? prop.Name : jsonPropertyAttribute.PropertyName;
                object propObj = prop.GetValue(obj, null);

                if 
                (
                    (pathElements.Length > 1)
                    && (nodeName == pathElements[0])
                    && propType.IsGenericType
                    && (propType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                )
                {
                    IDictionary dict = prop.GetValue(obj, null) as IDictionary;
                    Type valueType = propType.GetGenericArguments()[1];
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (entry.Key.ToString() == pathElements[1])
                        {
                            return GetObjectForConfigPath(entry.Value, string.Join(".", pathElements, 2, pathElements.Length - 2));
                        }
                    }
                }

                if (nodeName != pathElements[0])
                {
                    continue;
                }

                if (pathElements.Length > 1)
                {
                    return GetObjectForConfigPath(propObj, string.Join(".", pathElements, 1, pathElements.Length - 1));
                }

                return propObj;
            }

            return obj;
        }

        private void RemoveValueControls(Panel panel)
        {
            foreach(Control control in panel.Controls)
            {
                TextBox textBox = control as TextBox;
                if (textBox == null)
                {
                    continue;
                }
                else
                {
                    textBox.Validating -= ValueTextBoxValidating;
                }

                Button button = control as Button;
                if (button == null)
                {
                    continue;
                }
                else
                {
                    button.Click -= ValueButtonClickAction;
                }
            }

            panel.Controls.Clear();
            valueButtonActions.Clear();
        }

        private void CreateValueControls(Panel panel, object value, Type valueType, Configuration.ConfigEditorInfoConfig valueProperties)
        {
            RemoveValueControls(panel);

            if (valueType.IsArray)
            {
                Button editArrayButton = CreateValueButton("Edit Array...", () => { return; });
                panel.Controls.Add(editArrayButton);
                return;
            }

            if (valueType.Namespace.StartsWith("System") && valueType.Name.Contains(typeof(Dictionary<,>).Name))
            {
                Button editArrayButton = CreateValueButton("Add Entry...", () => { return; });
                panel.Controls.Add(editArrayButton);
                return;
            }

            if (!valueType.Namespace.StartsWith("System") && !valueType.Name.Contains(typeof(ConfigDictionaryEntry<,>).Name))
            {
                return;
            }

            if (valueType.Name.Contains(typeof(ConfigDictionaryEntry<,>).Name))
            {
                Button editArrayButton = CreateValueButton("Remove Entry", () => { return; });
                panel.Controls.Add(editArrayButton);

                if (!valueType.GetGenericArguments()[1].Namespace.StartsWith("System"))
                {
                    return;
                }
            }

            Control valueDisplayControl = CreateValueDisplayControl(value, valueType);
            if (valueDisplayControl == null)
            {
                return;
            }

            System.Windows.Forms.Label valueLabel = new System.Windows.Forms.Label();
            valueLabel.Text = "Value:";
            Size valueLabelSize = TextRenderer.MeasureText(valueLabel.Text, valueLabel.Font);
            valueLabel.Width = valueLabelSize.Width;
            valueLabel.Padding = new Padding(0, 6, 0, 0);
            panel.Controls.Add(valueLabel);

            panel.Controls.Add(valueDisplayControl);

            if (valueProperties.Unit == "")
            {
                return;
            }

            System.Windows.Forms.Label unitLabel = new System.Windows.Forms.Label();
            unitLabel.Text = valueProperties.Unit;

            if ((valueProperties.Max < double.MaxValue) || (valueProperties.Min > double.MinValue))
            {
                unitLabel.Text += " (" + valueProperties.Min + "..." + valueProperties.Max + ")";
            }

            Size unitLabelSize = TextRenderer.MeasureText(unitLabel.Text, unitLabel.Font);
            unitLabel.Width = unitLabelSize.Width;
            unitLabel.Padding = new Padding(0, 6, 0, 0);
            panel.Controls.Add(unitLabel);
        }

        private Control CreateValueDisplayControl(object value, Type valueType)
        {
            if (valueType == typeof(bool))
            {
                CheckBox valueCheckBox = new CheckBox();
                valueCheckBox.Text = "";
                valueCheckBox.Width = 32;

                valueCheckBox.Checked = (bool)value;

                return valueCheckBox;
            }

            TextBox valueTextBox = new TextBox();
            valueTextBox.Width = 150;
            valueTextBox.Validating += ValueTextBoxValidating;

            valueTextBox.Text = value.ToString();

            return valueTextBox;
        }

        private Button CreateValueButton(string text, Action onClickAction)
        {
            if (valueButtonActions.ContainsKey(text))
            {
                throw new InvalidOperationException("A button with that text was already added.");
            }

            Button button = new Button();
            button.Text = text;
            Size buttonSize = TextRenderer.MeasureText(text, button.Font);
            button.Width = buttonSize.Width + 32;

            button.Click += ValueButtonClickAction;
            valueButtonActions.Add(text, onClickAction);

            return button;
        }

        private void ValueButtonClickAction(object sender, EventArgs e)
        {
            Button button = sender as Button;

            if (!valueButtonActions.ContainsKey(button.Text))
            {
                throw new InvalidOperationException("A button with that text does not have an action assigned to it.");
            }

            valueButtonActions[button.Text]();
        }

        private void ValueTextBoxValidating(object sender, CancelEventArgs e)
        {
            if (isClosing)
            {
                return;
            }

            TextBox textBox = sender as TextBox;

            string configPath = GetConfigPathForTreeNode(configTreeView.SelectedNode);
            Configuration.ConfigEditorInfoConfig nodeConfigInfo = GetConfigInfoForPath(configPath);
            Type configType = configTypes[configTreeView.SelectedNode];

            try
            {
                object newValueObj = Convert.ChangeType(textBox.Text, configType);

                if (double.TryParse(newValueObj.ToString(), out double newValue))
                {
                    if (newValue > nodeConfigInfo.Max)
                    {
                        throw new InvalidOperationException("New value must be less than or equal to " + nodeConfigInfo.Max);
                    }
                    if (newValue < nodeConfigInfo.Min)
                    {
                        throw new InvalidOperationException("New value must be greather than or equal to " + nodeConfigInfo.Min);
                    }
                }
            }
            catch (FormatException)
            {
                e.Cancel = true;
                MessageBox.Show("Invalid entry. The value must be a " + configType.Name + ".", "Invalid Config Change", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch(Exception ex)
            {
                e.Cancel = true;
                MessageBox.Show(ex.Message, "Invalid Config Change", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}