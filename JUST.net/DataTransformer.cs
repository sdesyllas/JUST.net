﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Linq;

namespace JUST
{
    public class DataTransformer
    {
        public static string Transform(string transformer, string inputJson)
        {
            return Parse(transformer, inputJson, null, null);
        }

        private static string Parse(string transformer, string inputJson, JArray array, JToken currentArrayElement)
        {
            int startIndex = 0, index = 0;

            while ((startIndex < transformer.Length) && (index = transformer.IndexOf('#', startIndex)) != -1)
            {
                string functionString = GetFunctionString(transformer, index);

                if (functionString != null)
                {
                    if (functionString.Contains("loop"))
                    {
                        string loopArgsInclusive = GetLoopArguments(index, transformer, true);
                        string loopArgs = GetLoopArguments(index, transformer, false);

                        object result = EvaluateFunction(functionString, inputJson, array, currentArrayElement, loopArgs);
                        string evaluatedFunction = result.ToString();

                        StringBuilder builder = new StringBuilder(transformer);
                        builder.Remove(index-1, loopArgsInclusive.Length+1);
                        builder.Insert(index-1, evaluatedFunction);
                        transformer = builder.ToString();

                        startIndex = index + evaluatedFunction.Length;
                    }
                    else
                    {
                        object result = EvaluateFunction(functionString, inputJson, array, currentArrayElement, null);
                        string evaluatedFunction = result.ToString();

                        if (!string.IsNullOrEmpty(evaluatedFunction))
                        {
                            StringBuilder builder = new StringBuilder(transformer);
                            builder.Remove(index, functionString.Length);
                            builder.Insert(index, evaluatedFunction);
                            transformer = builder.ToString();

                            startIndex = index + evaluatedFunction.Length;
                        }
                    }
                }
                else
                    break;
            }

            return transformer;
        }

        private static string GetLoopArguments(int startIdex, string input,bool inclusive)
        {
            string loopArgs = string.Empty;

            int openBrackettCount = 0;
            int closebrackettCount = 0;

            int bStartIndex = 0;
            int bEndIndex = 0;
            for (int i = startIdex; i < input.Length; i++)
            {
                char currentChar = input[i];

                if (currentChar == '{')
                {
                    if (openBrackettCount == 0)
                        bStartIndex = i;

                    openBrackettCount++;
                }

                if (currentChar == '}')
                {
                    bEndIndex = i;
                    closebrackettCount++;
                }
               
                if (openBrackettCount > 0 && openBrackettCount == closebrackettCount)
                {
                    if(!inclusive)
                        loopArgs = input.Substring(bStartIndex, bEndIndex - bStartIndex + 1);
                    else
                        loopArgs = input.Substring(startIdex, bEndIndex - startIdex + 1);
                    break;
                }
            }

            if (inclusive && loopArgs == string.Empty)
                return "#loop";

            return loopArgs;
        }

        private static object EvaluateFunction(string functionString, string inputJson, JArray array, JToken currentArrayElement,
                    string loopArgumentString)
        {
            object output = null;

            functionString = functionString.Trim().Substring(1);

            int indexOfStart = functionString.IndexOf("(");

            if (indexOfStart != -1)
            {
                string functionName = functionString.Substring(0, indexOfStart);

                string argumentString = functionString.Substring(indexOfStart + 1, functionString.Length - indexOfStart - 2);

                string[] arguments = ExpressionHelper.GetArguments(argumentString);

                List<object> listParameters = new List<object>();

                if (arguments != null && arguments.Length > 0)
                {
                    foreach (string argument in arguments)
                    {
                        string trimmedArgument = argument;

                        if (argument.Contains("#"))
                            trimmedArgument = argument.Trim();

                        if (trimmedArgument.StartsWith("#"))
                        {
                            listParameters.Add(EvaluateFunction(trimmedArgument, inputJson, array, currentArrayElement, loopArgumentString));
                        }
                        else
                        {
                            listParameters.Add(trimmedArgument);
                        }
                    }
                }

                listParameters.Add(new JUSTContext(inputJson));
                var parameters = listParameters.ToArray();

                if (functionName == "loop")
                {
                    output = GetLoopResult(parameters, loopArgumentString);
                }
                else if (functionName == "currentvalue" || functionName == "currentindex" || functionName == "lastindex"
                    || functionName == "lastvalue")
                    output = caller("JUST.Transformer", functionName, new object[] { array, currentArrayElement });
                else if (functionName == "currentvalueatpath" || functionName == "lastvalueatpath")
                    output = caller("JUST.Transformer", functionName, new object[] { array, currentArrayElement, arguments[0] });
                else if (functionName == "customfunction")
                    output = CallCustomFunction(parameters);
                else if (functionName == "xconcat" || functionName == "xadd" || functionName == "mathequals" || functionName == "mathgreaterthan" || functionName == "mathlessthan"
                    || functionName == "mathgreaterthanorequalto"
                    || functionName == "mathlessthanorequalto" || functionName == "stringcontains" ||
                    functionName == "stringequals")
                {
                    object[] oParams = new object[1];
                    oParams[0] = parameters;
                    output = caller("JUST.Transformer", functionName, oParams);
                }
                else
                    output = caller("JUST.Transformer", functionName, parameters);
            }
            return output;
        }

        private static string GetStringValue(object o)
        {
            if (o is JToken token)
            {
                return token.ToString(Formatting.None);
            }
            if (o is IEnumerable<object> list)
            {
                return string.Join(",", list.Select(el => el.ToString()));
            }
            return o.ToString();
        }

        private static string GetLoopResult(object[] parameters,string loopArgumentString)
        {
            string returnString = string.Empty;

            if (parameters.Length < 2)
                throw new Exception("Incorrect number of parameters for function #Loop");

            JToken token = (parameters[parameters.Length - 1] as JUSTContext).Input;
            JToken selectedToken = token.SelectToken(parameters[0].ToString());

            if (selectedToken.Type != JTokenType.Array)
                throw new Exception("The JSONPath argument inside a #loop function must be an Array");

            JArray selectedArr = selectedToken as JArray;

            string seperator = Environment.NewLine;

            if (parameters.Length == 3)
                seperator = parameters[1].ToString();

            foreach (JToken arrToken in selectedToken.Children())
            {
                string parsedrecord = Parse(loopArgumentString, token.ToString(Formatting.None), selectedArr, arrToken);

                returnString += parsedrecord.Substring(1, parsedrecord.Length - 2);
                returnString += seperator;
            }

            return returnString;
        }

        private static string GetFunctionString(string input, int startIndex)
        {
            string functionString = string.Empty;

            int brackettOpenCount = 0;
            int brackettClosedCount = 0;

            for(int i = startIndex; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '(')
                    brackettOpenCount++;
                if (c == ')')
                    brackettClosedCount++;

                if( brackettClosedCount > 0 && brackettClosedCount == brackettOpenCount)
                {
                    functionString = input.Substring(startIndex, i - startIndex + 1);
                    break;
                }
            }

            return functionString;
        }

        private static object CallCustomFunction(object[] parameters)
        {
            object[] customParameters = new object[parameters.Length - 3];
            string functionString = string.Empty;
            string dllName = string.Empty;
            int i = 0;
            foreach (object parameter in parameters)
            {
                if (i == 0)
                    dllName = parameter.ToString();
                else if (i == 1)
                    functionString = parameter.ToString();
                else
                if (i != (parameters.Length - 1))
                    customParameters[i - 2] = parameter;

                i++;
            }

            int index = functionString.LastIndexOf(".");

            string className = functionString.Substring(0, index);
            string functionName = functionString.Substring(index + 1, functionString.Length - index - 1);

            className = className + "," + dllName;

            return caller(className, functionName, customParameters);
        }

        private static object caller(string myclass, string mymethod, object[] parameters)
        {
            Assembly assembly = null;
            return ReflectionHelper.caller(assembly, myclass, mymethod, parameters, true, new JUSTContext());
        }        
    }
}
