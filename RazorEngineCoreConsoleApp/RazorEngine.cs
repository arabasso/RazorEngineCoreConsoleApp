using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RazorEngineCoreConsoleApp
{
    public class TemplateNotFoundException :
        Exception
    {
    }

    public class TemplateCompilationException :
        Exception
    {
        public List<Diagnostic> Diagnostics { get; }

        public TemplateCompilationException(
            IEnumerable<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics.ToList();
        }
    }

    public class RazorEngine
    {
        private readonly Dictionary<string, Type>
            _templateTypes = new Dictionary<string, Type>();

        private ITemplateFactory _factory;
        public ITemplateFactory Factory => _factory ??= new TemplateFactory();

        public void AddTemplate(
            string name,
            string code)
        {
            AddTemplate(typeof(TemplateBase), name, code);
        }

        public void AddTemplate<TModel>(
            string name,
            string code)
        {
            AddTemplate(typeof(TemplateBase<TModel>), name, code);
        }

        public void AddTemplate<TTemplate, TModel>(
            string name,
            string code)
            where TTemplate : TemplateBase<TModel>
        {
            AddTemplate(typeof(TTemplate), name, code);
        }

        private Type AddTemplate(
            Type templateBaseType,
            string name,
            string code)
        {
            var source = Generate(templateBaseType, code);

            var memoryStream = Compile(templateBaseType, templateBaseType.Assembly.GetName().Name, source);

            var assembly = Assembly.Load(memoryStream.ToArray());

            var templateType = assembly.GetType($"{templateBaseType.Namespace}.Template");

            if (!_templateTypes.ContainsKey(name))
            {
                _templateTypes.Add(name, templateType);
            }

            else
            {
                _templateTypes[name] = templateType;
            }

            return templateType;
        }

        public async Task RunCompileAsync<TModel>(
            string name,
            string code,
            TModel model,
            TextWriter writer)
        {
            if (typeof(TModel).Namespace == null)
            {
                await RunCompileAsync<TemplateBase, object>(name, code, new AnonymousTypeWrapper(model), writer);

                return;
            }

            await RunCompileAsync<TemplateBase<TModel>, TModel>(name, code, model, writer);
        }

        public async Task<string> RunCompileAsync<TModel>(
            string name,
            string code,
            TModel model)
        {
            if (typeof(TModel).Namespace == null)
            {
                return await RunCompileAsync<TemplateBase, object>(name, code, new AnonymousTypeWrapper(model));
            }

            return await RunCompileAsync<TemplateBase<TModel>, TModel>(name, code, model);
        }

        public async Task<string> RunCompileAsync<TTemplate, TModel>(
            string name,
            string code,
            TModel model)
            where TTemplate : TemplateBase<TModel>
        {
            await using var writer = new StringWriter();

            await RunCompileAsync<TTemplate, TModel>(name, code, model, writer);

            return writer.GetStringBuilder().ToString();
        }

        public async Task RunCompileAsync<TTemplate, TModel>(
            string name,
            string code,
            TModel model,
            TextWriter writer)
            where TTemplate : TemplateBase<TModel>
        {
            if (!_templateTypes.TryGetValue(name, out var templateType))
            {
                templateType = AddTemplate(typeof(TTemplate), name, code);
            }

            await RunAsync<TTemplate, TModel>(templateType, model, writer);
        }

        public async Task<string> RunAsync<TModel>(
            string name,
            TModel model)
        {
            if (typeof(TModel).Namespace == null)
            {
                return await RunAsync<TemplateBase, object>(name, new AnonymousTypeWrapper(model));
            }


            return await RunAsync<TemplateBase<TModel>, TModel>(name, model);
        }

        public async Task RunAsync<TModel>(
            string name,
            TModel model,
            TextWriter writer)
        {
            if (typeof(TModel).Namespace == null)
            {
                await RunAsync<TemplateBase, object>(name, new AnonymousTypeWrapper(model), writer);

                return;
            }

            await RunAsync<TemplateBase<TModel>, TModel>(name, model, writer);
        }

        public async Task<string> RunAsync<TTemplate, TModel>(
            string name,
            TModel model)
            where TTemplate : TemplateBase<TModel>
        {
            await using var writer = new StringWriter();

            await RunAsync<TTemplate, TModel>(name, model, writer);

            return writer.GetStringBuilder().ToString();
        }

        public async Task RunAsync<TTemplate, TModel>(
            string name,
            TModel model,
            TextWriter writer)
            where TTemplate : TemplateBase<TModel>
        {
            if (!_templateTypes.TryGetValue(name, out var templateType))
            {
                throw new TemplateNotFoundException();
            }

            await RunAsync<TTemplate, TModel>(templateType, model, writer);
        }

        private async Task RunAsync<TTemplate, TModel>(
            Type templateType,
            TModel model,
            TextWriter writer)
            where TTemplate : TemplateBase<TModel>
        {
            var template = (TTemplate)Factory.Create(templateType);

            template.Writer = writer;
            template.Model = model;

            await template.ExecuteAsync();
        }

        private string Generate(
            Type templateBaseType,
            string template)
        {
            var engine = RazorProjectEngine.Create(
                RazorConfiguration.Default,
                RazorProjectFileSystem.Create(@"."),
                builder =>
                {
                    builder.SetNamespace(templateBaseType.Namespace);
                    builder.SetBaseType(Regex.Replace(templateBaseType.ToString(), @"`\d+\[", "<").Replace(']', '>'));
                });

            var document = RazorSourceDocument.Create(template, Path.GetRandomFileName());

            var codeDocument = engine.Process(
                document,
                null,
                new List<RazorSourceDocument>(),
                new List<TagHelperDescriptor>());

            return codeDocument.GetCSharpDocument().GeneratedCode;
        }

        private MemoryStream Compile(
            Type templateBaseType,
            string assemblyName,
            string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[]
                {
                    syntaxTree
                },
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(templateBaseType.Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(DynamicObject).Assembly.Location),
                    MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("Microsoft.CSharp")).Location),
                    MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location),
                    MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var memoryStream = new MemoryStream();

            var emitResult = compilation.Emit(memoryStream);

            if (!emitResult.Success)
            {
                throw new TemplateCompilationException(emitResult.Diagnostics);
            }

            memoryStream.Position = 0;

            return memoryStream;
        }
    }

    public interface ITemplateFactory
    {
        object Create(Type type);
    }

    public sealed class TemplateFactory :
        ITemplateFactory
    {
        public object Create(
            Type templateType)
        {
            return Activator.CreateInstance(templateType)
                   ?? throw new NullReferenceException();
        }
    }

    public abstract class TemplateBase<TModel>
    {
        public TextWriter Writer;
        public TModel Model { get; set; }

        public abstract Task ExecuteAsync();

        public void WriteLiteral(
            string literal)
        {
            Writer.Write(literal);
        }

        public void Write(
            object obj)
        {
            Writer.Write(obj);
        }
    }

    public abstract class TemplateBase :
        TemplateBase<dynamic>
    {
    }

    public class AnonymousTypeWrapper :
        DynamicObject
    {
        private readonly Dictionary<string, object>
            _customProperties = new Dictionary<string, object>();

        private readonly object _currentObject;

        public AnonymousTypeWrapper(
            object sealedObject)
        {
            _currentObject = sealedObject;
        }

        private PropertyInfo GetPropertyInfo(string propertyName)
        { 
            return _currentObject.GetType().GetProperty(propertyName);
        } 

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var prop = GetPropertyInfo(binder.Name);
            if(prop != null)
            {
                result = prop.GetValue(_currentObject);
                return true;
            }
            result = _customProperties[binder.Name];
            return true;          
        }      

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            var prop = GetPropertyInfo(binder.Name);
            if(prop != null)
            {
                prop.SetValue(_currentObject, value);
                return true;
            }
            if(_customProperties.ContainsKey(binder.Name))
                _customProperties[binder.Name] = value;
            else
                _customProperties.Add(binder.Name, value);
            return true;          
        } 
    }
}
