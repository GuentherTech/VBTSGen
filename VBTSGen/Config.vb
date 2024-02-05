Imports System.IO
Imports System.Text.Json.Nodes

Public Class Config
    Public Property InputDir As String
    Public Property OutputDir As String
    
    Public Shared Function Load() As Config
        Dim node = JsonNode.Parse(File.ReadAllText("vbtsgenconfig.json"))
        Return New Config With {
            .InputDir = node!InputDir,
            .OutputDir = node!OutputDir
        }
    End Function
End Class