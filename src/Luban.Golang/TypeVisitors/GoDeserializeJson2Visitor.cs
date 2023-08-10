using Luban.Types;
using Luban.TypeVisitors;

namespace Luban.Golang.TypeVisitors;

public class GoDeserializeJson2Visitor : DecoratorFuncVisitor<string, string, string>
{
    public static GoDeserializeJson2Visitor Ins { get; } = new();

    public override string DoAccept(TType type, string varName, string bufName)
    {
        if (type.IsNullable)
        {
            return $"{{ if {bufName} == nil {{ return }} else {{ var __x__ {type.Apply(GoUnderlyingDeclaringTypeNameVisitor.Ins)};  {type.Apply(GoDeserializeJsonUndering2Visitor.Ins, "__x__", bufName)}; {varName} = {(type.Apply(GoIsPointerTypeVisitor.Ins) ? "&" : "")}__x__ }}}}";
        }
        else
        {
            return type.Apply(GoDeserializeJsonUndering2Visitor.Ins, varName, bufName);
        }
    }
}