using System;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace RuleParse
{
    class Program
    {
        private static readonly StringBuilder sb = new System.Text.StringBuilder();
        private static XmlNamespaceManager nsmgr = null;
        private static readonly Dictionary<string, string> sqlStringMapper = new Dictionary<string, string>();
        static void Main(string[] args)
        {
            Console.WriteLine("Loading rule...");
            var filePath = string.Empty;

            if (args.Length > 0) 
            {
                filePath = args[0];
            }
            
            sqlStringMapper.Add("greaterOrEqual", ">=");
            sqlStringMapper.Add("greater", ">");
            sqlStringMapper.Add("lessOrEqual", "<=");
            sqlStringMapper.Add("less", "<");
            sqlStringMapper.Add("equal", "=");
            sqlStringMapper.Add("notEqual", "<>");
            sqlStringMapper.Add("doesNotContain", "NOT LIKE '%{0}%'");
            sqlStringMapper.Add("contains", "LIKE '%{0}%'");
            sqlStringMapper.Add("startsWith", "LEFT('{0}', 1) = '{1}'");
            sqlStringMapper.Add("doesNotStartWith", "LEFT('{0}', 1) <> '{1}'");
            sqlStringMapper.Add("endsWith", "RIGHT('{0}', 1) = '{1}'");
            sqlStringMapper.Add("doesNotEndWith", "RIGHT('{0}', 1) <> '{1}'");
            sqlStringMapper.Add("isNull", "IS NULL");
            sqlStringMapper.Add("isNotNull", "IS NOT NULL");


            if (string.IsNullOrEmpty(filePath))
            {
                filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "sampleData.xml");
            }
            

            Console.WriteLine("Parsing codeeffects rule.");
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine();
            Console.WriteLine();

            var doc = new XmlDocument();
            nsmgr = new XmlNamespaceManager(doc.NameTable);
            doc.Load(filePath);
            sb.AppendLine("SELECT * FROM [InsertYourTableName] WHERE ");
            XmlNode root = doc.DocumentElement;
            nsmgr.AddNamespace("df", "https://codeeffects.com/schemas/rule/41");
            nsmgr.AddNamespace("ui", "https://codeeffects.com/schemas/ui/4");
            XmlNodeList clauses = doc.SelectNodes("descendant::df:clause", nsmgr);
            if (clauses != null)
                foreach (XmlNode clause in clauses)
                {
                    XmlNode firstNode = clause.FirstChild;
                    var nodeList = clause.SelectNodes("./df:or|./df:and", nsmgr);
                    if (nodeList != null)
                        foreach (XmlNode node in nodeList)
                        {
                            Parse(node.Name, node);
                        }

                    Console.Write(sb.ToString());
                }
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------");

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private static void Parse(string previousOperator, XmlNode node)
        {
            //get the conditions and parse them
            XmlNodeList conditions = node.SelectNodes("./df:condition", nsmgr);
            foreach (XmlNode condition in conditions)
            {
                ParseCondition(condition);
                //either OR or AND the result for the next condition
                sb.Append("\u0020" + previousOperator + "\u0020");
            }

            //get the immediate children operator nodes
            var nodeList = node.SelectNodes("./df:or|./df:and", nsmgr);

            //if there are no direct children, remove the tailing operator statement from the string.
            if (nodeList.Count == 0)
            {
                sb.Remove(sb.Length - previousOperator.Length - 2, previousOperator.Length + 2);
            }
            else
            {
                for (var i = 0; i < nodeList.Count; i++)
                {
                    XmlNode next = nodeList[i];

                    if(previousOperator == "or")
                    {
                        sb.AppendLine("(");
                    }
                    if (next.Name == "and")
                    {
                        Parse("and", next); 
                    }
                    else if (next.Name == "or")
                    {
                        if (previousOperator == "and")
                        {
                            sb.Append("(");
                            Parse("or", next);
                            sb.AppendLine(")");
                        }
                        else
                        {
                            Parse("or", next);
                        }
                    }
                    if (previousOperator == "or")
                    {
                        if (i == nodeList.Count - 1)
                        {
                            sb.Append(")");
                        }
                        else
                        {
                            sb.AppendLine(") OR ");
                        }
                    }
                }
            }
        }

        private static void ParseCondition(XmlNode condition)
        {
            var property1Node = condition.SelectSingleNode("descendant::df:property", nsmgr);
            var property1Name = string.Empty;
            //get the name of the property
            if (property1Node?.Attributes != null)
            {
                property1Name = property1Node.Attributes.GetNamedItem("name").Value;
            }
            if (condition.Attributes == null)
            {
                throw new Exception("Condition must have type");
            }
            var equalityOperator = condition.Attributes.GetNamedItem("type").Value;
            var valueNode = condition.SelectSingleNode("descendant::df:value", nsmgr);
            //we know a value is being compared against a property
            if (valueNode != null) //we know there is a value
            {
                var valueDataTypeAttribute = valueNode.Attributes.GetNamedItem("type");
                //if we need a template for string comparison. 
                if (equalityOperator == "doesNotContain"
                    || equalityOperator == "contains")
                {
                    sb.Append($" {property1Name}  {string.Format($"{sqlStringMapper[equalityOperator]}", valueNode.InnerXml)}");
                }
                else if (equalityOperator == "startsWith" || equalityOperator == "endsWith" ||
                         equalityOperator == "doesNotStartWith" || equalityOperator == "doesNotEndWith")
                {
                    sb.Append($" {string.Format($"{sqlStringMapper[equalityOperator]}", property1Name, valueNode.InnerXml)}");
                }
                else
                {
                    if (valueDataTypeAttribute != null) //if a data type is specified
                    {
                        switch (valueDataTypeAttribute.Value)
                        {
                            case "System.Boolean" when valueNode.InnerXml.ToLower() == "true":
                            {
                                sb.Append(string.IsNullOrEmpty(property1Name)
                                    ? $" 1 {sqlStringMapper[equalityOperator]} 1"
                                    : $" {property1Name} {sqlStringMapper[equalityOperator]} 1");

                                break;
                            }
                            case "System.Boolean" when string.IsNullOrEmpty(property1Name):
                                sb.Append($" 1 {sqlStringMapper[equalityOperator]} 1");
                                break;
                            case "System.Boolean":
                                sb.Append($" {property1Name} {sqlStringMapper[equalityOperator]} 1");
                                break;
                            case "numeric":
                                sb.Append($" {property1Name} {sqlStringMapper[equalityOperator]} {valueNode.InnerXml}");
                                break;
                            //for enumerations
                            default:
                                sb.Append($" {property1Name} {sqlStringMapper[equalityOperator]} {valueNode.InnerXml}");
                                break;
                        }
                    }
                    else
                    {
                        sb.Append($" {property1Name} {sqlStringMapper[equalityOperator]} '{valueNode.InnerXml}'");
                    }
                }
            }
            else //no value node specified as right hand operand. Another property is expected to be evaluated.
            {
                //check if there is a property being compared as the right hand operand
                if (property1Node?.NextSibling != null)
                {
                    if (property1Node.NextSibling.Attributes != null)
                    {
                        var property2Name = property1Node.NextSibling.Attributes.GetNamedItem("name").Value;
                        sb.Append($" {property1Name} {sqlStringMapper[equalityOperator]} {property2Name}");
                    }
                    else
                    {
                        throw new Exception("There is no value or property to compare.");
                    }
                }
                else //check if checking for null or not null
                {
                    switch (equalityOperator)
                    {
                        case "isNull":
                            sb.Append($" {property1Name} IS NULL");
                            break;
                        case "isNotNull":
                            sb.Append($" {property1Name} IS NOT NULL");
                            break;
                    }
                }
            }
        }
    }
}
