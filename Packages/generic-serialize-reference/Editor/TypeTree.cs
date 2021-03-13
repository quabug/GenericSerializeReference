using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;

namespace GenericSerializeReference
{

    /// <summary>
    ///
    /// create tree structure for types
    ///
    /// "X": class
    /// "<-": inherit from
    /// "X(I)": X implement interface I
    ///
    ///   A <-+-- B(I) <---- C
    ///       |
    ///       +-- D(I) <-+-- E(I)
    ///                 |
    ///                 +-- F
    ///
    /// type tree structure: A ( B ( C ) + D ( E + F ) )
    /// interface chain: I(B, D, E)
    /// </summary>
    public class TypeTree
    {
        class TypeDefinitionTokenComparer : IEqualityComparer<TypeDefinition>
        {
            public bool Equals(TypeDefinition x, TypeDefinition y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return new TypeKey(x).Equals(new TypeKey(y));
            }

            public int GetHashCode(TypeDefinition obj)
            {
                return new TypeKey(obj).GetHashCode();
            }
        }

        readonly struct TypeKey : IEquatable<TypeKey>
        {
            public readonly string ModuleName;
            public readonly MetadataToken Token;

            public TypeKey(TypeDefinition typeDefinition)
            {
                ModuleName = typeDefinition.Module.Name;
                Token = typeDefinition.MetadataToken;
            }

            public bool Equals(TypeKey other)
            {
                return ModuleName == other.ModuleName && Token.Equals(other.Token);
            }

            public override bool Equals(object obj)
            {
                return obj is TypeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ModuleName.GetHashCode() * 397) ^ Token.GetHashCode();
                }
            }
        }

        private class TypeTreeNode
        {
            public ISet<TypeDefinition> Interfaces { get; } = new HashSet<TypeDefinition>(new TypeDefinitionTokenComparer());
            public TypeDefinition Type { get; }
            public TypeTreeNode Base { get; set; }
            public List<TypeTreeNode> Subs { get; } = new List<TypeTreeNode>();

            public TypeTreeNode(TypeDefinition type) => Type = type;

            public override string ToString()
            {
                return $"{Type.Name}({Type.Module.Name})";
            }
        }

        private readonly Dictionary<TypeKey, TypeTreeNode> _typeTreeNodeMap;

        private readonly ILPostProcessorLogger _logger = new ILPostProcessorLogger();

        /// <summary>
        /// Create a type-tree from a collection of <paramref name="sourceTypes"/>
        /// </summary>
        /// <param name="sourceTypes">The source types of tree.</param>
        public TypeTree([NotNull] IEnumerable<TypeDefinition> sourceTypes)
        {
            _typeTreeNodeMap = new Dictionary<TypeKey, TypeTreeNode>();
            foreach (var type in sourceTypes.Where(t => t.IsClass || t.IsInterface)) CreateTypeTree(type);
        }

        // TODO:
        // /// <summary>
        // /// Create a type-tree from a collection of <paramref name="sourceTypes"/>
        // /// </summary>
        // /// <param name="sourceTypes">The source types of tree.</param>
        // /// <param name="baseTypes">Excluded any types from <paramref name="sourceTypes"/> which is not derived from base type.</param>
        // public TypeTree([NotNull] ICollection<TypeDefinition> sourceTypes, [NotNull] ICollection<TypeDefinition> baseTypes)
        // {
        // }

        /// <summary>
        /// Get all derived class type of <paramref name="baseType"/>.
        /// Ignore generic type argument if <paramref name="baseType"/> is a generic class with certain type argument.
        /// </summary>
        /// <param name="baseType"></param>
        /// <returns>Any type of classes derived from <paramref name="baseType"/> directly or indirectly.</returns>
        /// <exception cref="ArgumentException"></exception>
        public IEnumerable<TypeDefinition> GetAllDerived(TypeDefinition baseType)
        {
            _typeTreeNodeMap.TryGetValue(new TypeKey(baseType), out var node);
            if (node == null) throw new ArgumentException($"{baseType} is not part of this tree");
            return GetDescendantsAndSelf(node).Skip(1).Select(n => n.Type);

            IEnumerable<TypeTreeNode> GetDescendantsAndSelf(TypeTreeNode self)
            {
                yield return self;
                foreach (var childNode in
                    from sub in self.Subs
                    from type in GetDescendantsAndSelf(sub)
                    select type
                ) yield return childNode;
            }
        }

        /// <summary>
        /// Get directly derived class type of <paramref name="baseType"/>.
        /// Ignore generic type argument if <paramref name="baseType"/> is a generic class with certain type argument.
        /// </summary>
        /// <param name="baseType"></param>
        /// <returns>Any type of classes derived from <paramref name="baseType"/> directly.</returns>
        /// <exception cref="ArgumentException"></exception>
        public IEnumerable<TypeDefinition> GetDirectDerived(TypeDefinition baseType)
        {
            _typeTreeNodeMap.TryGetValue(new TypeKey(baseType), out var node);
            if (node == null) throw new ArgumentException($"{baseType} is not part of this tree");
            return node.Subs.Select(sub => sub.Type);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            foreach (var pair in _typeTreeNodeMap)
            {
                if (pair.Value.Base == null)
                {
                    var root = pair.Value;
                    ToString(root);
                }
            }
            return builder.ToString();

            void ToString(TypeTreeNode node, int indent = 0)
            {
                for (var i = 0; i < indent; i++) builder.Append("    ");
                builder.AppendLine(node.ToString());
                foreach (var child in node.Subs) ToString(child, indent + 1);
            }
        }

        TypeTreeNode CreateTypeTree(TypeDefinition type)
        {
            if (type == null) return null;
            if (_typeTreeNodeMap.TryGetValue(new TypeKey(type), out var node)) return node;

            var self = new TypeTreeNode(type);
            foreach (var @interface in type.Interfaces)
            {
                // HACK: some interface cannot be resolved? has been stripped by Unity?
                //       e.g. the interface `IEditModeTestYieldInstruction` of `ExitPlayMode`
                //            will resolve to null
                var interfaceDefinition = @interface.InterfaceType.Resolve();
                if (interfaceDefinition == null)
                    _logger.Warning($"Invalid interface definition {@interface.InterfaceType}({@interface.InterfaceType.Module}) on {type}");
                else
                    self.Interfaces.Add(interfaceDefinition);
            }

            var baseTypeDef = type.BaseType?.Resolve();
            if (baseTypeDef == null && type.BaseType != null)
                _logger.Warning($"Invalid base type definition {type.BaseType}({type.BaseType.Module}) on {type.Name}");
            var parent = CreateTypeTree(baseTypeDef);
            var uniqueInterfaces = new HashSet<TypeDefinition>(self.Interfaces, new TypeDefinitionTokenComparer());
            if (parent != null)
            {
                self.Base = parent;
                parent.Subs.Add(self);
                self.Interfaces.UnionWith(parent.Interfaces);
                uniqueInterfaces.ExceptWith(parent.Interfaces);
            }

            foreach (var @interface in uniqueInterfaces)
            {
                var typeKey = new TypeKey(@interface);
                if (!_typeTreeNodeMap.TryGetValue(typeKey, out var implementations))
                {
                    implementations = new TypeTreeNode(@interface);
                    _typeTreeNodeMap.Add(typeKey, implementations);
                }
                implementations.Subs.Add(self);
            }

            try
            {
                _typeTreeNodeMap.Add(new TypeKey(type), self);
            }
            catch
            {
                var duplicate = _typeTreeNodeMap[new TypeKey(type)];
                _logger.Error($"Duplicate type? {type} {duplicate.Type}");
            }
            return self;
        }
    }
}