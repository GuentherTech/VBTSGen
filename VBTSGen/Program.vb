Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Public Module Program
    Public Sub Main(args As String())
        Dim config As Config = Config.Load()
        If Not Directory.Exists(config.OutputDir) Then
            Directory.CreateDirectory(config.OutputDir)
        End If
        Dim types As New List(Of TSType)
        Dim importTypes As New List(Of (File As String, [Imports] As String()))
        For Each inputFile As String In Directory.EnumerateFiles(config.InputDir, "*.vb")
            Dim outputFile = CamelCase($"{Path.GetFileNameWithoutExtension(inputFile)}.ts")
            Dim content As String = File.ReadAllText(inputFile)
            Dim output As New StringBuilder
            Dim tree As CompilationUnitSyntax = SyntaxFactory.ParseCompilationUnit(content)
            Dim classes As IEnumerable(Of ClassBlockSyntax) = tree.DescendantNodes().OfType(Of ClassBlockSyntax)()
            For Each [class] As ClassBlockSyntax In classes
                Dim className As String = [class].ClassStatement.Identifier.Text
                output.AppendLine($"export type {className} = {{")
                Dim properties As IEnumerable(Of PropertyStatementSyntax) = [class].DescendantNodes().OfType(Of PropertyStatementSyntax)()
                Dim usedTypes As New List(Of String)
                For Each prop As PropertyStatementSyntax In properties
                    Dim ignoreAttr As AttributeListSyntax = prop.AttributeLists.FirstOrDefault(Function(a) a.Attributes.Any(Function(at) at.Name.ToString() = "JsonIgnore"))
                    If ignoreAttr IsNot Nothing Then
                        Continue For
                    End If
                    Dim nullableAttr As AttributeListSyntax = prop.AttributeLists.FirstOrDefault(Function(a) a.Attributes.Any(Function(at) at.Name.ToString() = "TsNullable"))
                    Dim propName As String = CamelCase(If(prop.Identifier.IsBracketed(), prop.Identifier.Text.Substring(1, prop.Identifier.Text.Length - 2), prop.Identifier.Text))
                    Dim tsTypeAttr As AttributeListSyntax = prop.AttributeLists.FirstOrDefault(Function(a) a.Attributes.Any(Function(at) at.Name.ToString() = "TsType"))
                    If tsTypeAttr IsNot Nothing Then
                        Dim tsType = tsTypeAttr.Attributes.FirstOrDefault(Function(a) a.Name.ToString() = "TsType")
                        Dim tsTypeArg = DirectCast(tsType.ArgumentList.Arguments.First().GetExpression(), LiteralExpressionSyntax).Token.ValueText
                        If tsType.ArgumentList.Arguments.Count > 1 Then
                            Dim importArg = tsType.ArgumentList.Arguments(1).GetExpression()
                            Dim import = DirectCast(importArg, LiteralExpressionSyntax).Token.Value
                            If import Then
                                importTypes.Add((outputFile, {tsTypeArg}))
                            End If
                        End If
                        output.AppendLine($"    {propName}{If(nullableAttr Is Nothing, "", "?")}: {tsTypeArg}")
                        Continue For
                    End If
                    Dim type = GetTsType(DirectCast(prop.AsClause, SimpleAsClauseSyntax).Type)
                    output.AppendLine($"    {propName}{If(nullableAttr Is Nothing, "", "?")}: {type.Result}")
                    usedTypes.AddRange(type.Types)
                Next
                importTypes.Add((outputFile, usedTypes.ToArray()))
                output.AppendLine("}")
                output.AppendLine()
                types.Add(New TSType With {.Name = className, .File = outputFile})
            Next
            Dim enums As IEnumerable(Of EnumBlockSyntax) = tree.DescendantNodes().OfType(Of EnumBlockSyntax)()
            For Each [enum] As EnumBlockSyntax In enums
                Dim enumName As String = [enum].EnumStatement.Identifier.Text
                output.AppendLine($"export enum {enumName} {{")
                Dim members As IEnumerable(Of EnumMemberDeclarationSyntax) = [enum].DescendantNodes().OfType(Of EnumMemberDeclarationSyntax)()
                Dim current As Integer = 0
                For Each member As EnumMemberDeclarationSyntax In members
                    output.AppendLine($"    {member.Identifier.Text} = {If(member.Initializer?.Value, current)},")
                    If member.Initializer?.Value Is Nothing Then
                        current += 1
                    End If
                Next
                output.AppendLine("}")
                output.AppendLine()
                types.Add(New TSType With {.Name = enumName, .File = outputFile})
            Next
            If classes.Any() OrElse enums.Any() Then
                File.WriteAllText(Path.Combine(config.OutputDir, outputFile), output.ToString())
            End If
        Next
        For Each f In importTypes
            Dim out = Path.Combine(config.OutputDir, f.File)
            Dim importContent As New StringBuilder
            For Each imp In f.Imports
                Dim tsType = types.FirstOrDefault(Function(t) t.Name = imp)
                If tsType IsNot Nothing Then
                    If f.File <> tsType.File Then
                        importContent.AppendLine($"import {{ {imp} }} from './{Path.GetFileNameWithoutExtension(tsType.File)}'")
                    End If
                Else
                    importContent.AppendLine($"import {{ {imp} }} from './{CamelCase(imp)}'")
                End If
            Next
            If importContent.Length > 0 Then
                importContent.AppendLine()
            End If
            File.WriteAllText(out, importContent.ToString() & File.ReadAllText(out))
        Next
    End Sub

    Private Function GetTsType(type As TypeSyntax) As (Result As String, Types As String())
        Dim predefinedType As PredefinedTypeSyntax = TryCast(type, PredefinedTypeSyntax)
        If predefinedType IsNot Nothing Then
            Dim res As String
            Select Case predefinedType.Keyword.Text
                Case "String"
                    res = "string"
                Case "Integer"
                    res = "number"
                Case "Date"
                    res = "Date"
                Case "Double"
                    res = "number"
                Case "Boolean"
                    res = "boolean"
                Case "Byte"
                    res = "number"
                Case "Short"
                    res = "number"
                Case "Single"
                    res = "number"
                Case "Long"
                    res = "number"
                Case "Decimal"
                    res = "number"
                Case Else
                    res = "any"
            End Select
            Return (res, Array.Empty(Of String)())
        End If
        Dim arrayType As ArrayTypeSyntax = TryCast(type, ArrayTypeSyntax)
        If arrayType IsNot Nothing Then
            Dim elType As PredefinedTypeSyntax = TryCast(arrayType.ElementType, PredefinedTypeSyntax)
            If elType IsNot Nothing AndAlso elType.Keyword.Text = "Byte" Then
                Return ("string", Array.Empty(Of String)())
            End If
            Dim arrType = GetTsType(arrayType.ElementType)
            Return ($"{arrType.Result}[]", arrType.Types)
        End If
        Dim genericType As GenericNameSyntax = TryCast(type, GenericNameSyntax)
        If genericType IsNot Nothing Then
            If genericType.Identifier.Text = "List" Then
                Dim listType = GetTsType(genericType.TypeArgumentList.Arguments.Single())
                Return ($"{listType.Result}[]", listType.Types)
            End If
            Dim genType = GetTsType(SyntaxFactory.ParseTypeName(genericType.Identifier.Text))
            Dim names As New List(Of String)
            Dim types As New List(Of String)
            For Each arg As TypeSyntax In genericType.TypeArgumentList.Arguments
                Dim argType = GetTsType(arg)
                names.Add(argType.Result)
                types.AddRange(argType.Types)
            Next
            types.AddRange(genType.Types)
            Return ($"{genType.Result}<{String.Join(", ", names)}>", types.ToArray())
        End If
        Dim identifier As IdentifierNameSyntax = TryCast(type, IdentifierNameSyntax)
        If identifier IsNot Nothing Then
            Return (identifier.Identifier.Text, {identifier.Identifier.Text})
        End If
        Return ("any", Array.Empty(Of String)())
    End Function

    Private Function CamelCase(str As String) As String
        Return JsonNamingPolicy.CamelCase.ConvertName(str)
    End Function
End Module