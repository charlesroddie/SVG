﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Xml;
using Svg.Transforms;

namespace Svg
{
    /// <summary>
    /// The base class of which all SVG elements are derived from.
    /// </summary>
    public abstract partial class SvgElement : ISvgElement, ISvgTransformable, ICloneable, ISvgNode
    {
        internal const int StyleSpecificity_PresAttribute = 0;
        internal const int StyleSpecificity_InlineStyle = 1 << 16;
        internal SvgElement _parent;
        private string _elementName;
        private SvgAttributeCollection _attributes;
        private EventHandlerList _eventHandlers;
        private SvgElementCollection _children;
        private static readonly object _loadEventKey = new object();
        private SvgCustomAttributeCollection _customAttributes;
        private List<ISvgNode> _nodes = new List<ISvgNode>();

        private Dictionary<string, SortedDictionary<int, string>> _styles = new Dictionary<string, SortedDictionary<int, string>>();

        /// <summary>
        /// Add style.
        /// </summary>
        /// <param name="name">The style name.</param>
        /// <param name="value">The style value.</param>
        /// <param name="specificity">The specificity value.</param>
        public void AddStyle(string name, string value, int specificity)
        {
            SortedDictionary<int, string> rules;
            if (!_styles.TryGetValue(name, out rules))
            {
                rules = new SortedDictionary<int, string>();
                _styles[name] = rules;
            }
            while (rules.ContainsKey(specificity)) ++specificity;
            rules[specificity] = value;
        }

        /// <summary>
        /// Flush styles.
        /// </summary>
        /// <param name="children">If true, flush styles to the children.</param>
        public void FlushStyles(bool children = false)
        {
            FlushStyles();
            if (children)
                foreach (var child in Children)
                    child.FlushStyles(children);
        }

        private void FlushStyles()
        {
            if (_styles.Any())
            {
                var styles = new Dictionary<string, SortedDictionary<int, string>>();
                foreach (var s in _styles)
                    if (!SvgElementFactory.SetPropertyValue(this, string.Empty, s.Key, s.Value.Last().Value, OwnerDocument, true))
                        styles.Add(s.Key, s.Value);
                _styles = styles;
            }
        }

        public bool ContainsAttribute(string name)
        {
            SortedDictionary<int, string> rules;
            return (this.Attributes.ContainsKey(name) || this.CustomAttributes.ContainsKey(name) ||
                (_styles.TryGetValue(name, out rules)) && (rules.ContainsKey(StyleSpecificity_InlineStyle) || rules.ContainsKey(StyleSpecificity_PresAttribute)));
        }
        public bool TryGetAttribute(string name, out string value)
        {
            object objValue;
            if (this.Attributes.TryGetValue(name, out objValue))
            {
                value = objValue.ToString();
                return true;
            }
            if (this.CustomAttributes.TryGetValue(name, out value)) return true;
            SortedDictionary<int, string> rules;
            if (_styles.TryGetValue(name, out rules))
            {
                // Get staged styles that are
                if (rules.TryGetValue(StyleSpecificity_InlineStyle, out value)) return true;
                if (rules.TryGetValue(StyleSpecificity_PresAttribute, out value)) return true;
            }
            return false;
        }

        protected internal static HttpClient HttpClient { get; } = new HttpClient();

        /// <summary>
        /// Gets the namespaces that element has.
        /// </summary>
        /// <value>Key is prefix and value is namespace.</value>
        public Dictionary<string, string> Namespaces { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets the elements namespace as a string.
        /// </summary>
        protected internal string ElementNamespace { get; protected set; } = SvgNamespaces.SvgNamespace;

        /// <summary>
        /// Gets the name of the element.
        /// </summary>
        protected internal string ElementName
        {
            get
            {
                if (string.IsNullOrEmpty(this._elementName))
                {
                    // There is special case for SvgDocument as valid attribute is only set on SvgFragment.
                    if (SvgElements.ElementNames.TryGetValue(this.GetType(), out var elementName))
                    {
                        this._elementName = elementName;
                    }
                    else if (this is SvgDocument)
                    {
                        // The SvgDocument does not have SvgElement attribute set, instead the attitude is used on SvgFragment so there would be duplicate im dictionary.
                        // The SvgDocument is not valid Svg element (that is SvgFragment) and is mainly used as abstraction for document reading and writing.
                        // The ElementName for SvgDocument is set explicitly here as that is the exception to attribute convention used accross codebase.
                        this._elementName = "svg";
                    }
                }

                return this._elementName;
            }
            internal set { this._elementName = value; }
        }

        /// <summary>
        /// Gets or sets the color <see cref="SvgPaintServer"/> of this element which drives the currentColor property.
        /// </summary>
        [SvgAttribute("color")]
        public virtual SvgPaintServer Color
        {
            get { return GetAttribute("color", true, SvgPaintServer.NotSet); }
            set { Attributes["color"] = value; }
        }

        /// <summary>
        /// Gets or sets the content of the element.
        /// </summary>
        private string _content;
        public virtual string Content
        {
            get
            {
                return _content;
            }
            set
            {
                if (_content != null)
                {
                    var oldVal = _content;
                    _content = value;
                    if (_content != oldVal)
                        OnContentChanged(new ContentEventArgs { Content = value });
                }
                else
                {
                    _content = value;
                    OnContentChanged(new ContentEventArgs { Content = value });
                }
            }
        }

        /// <summary>
        /// Gets an <see cref="EventHandlerList"/> of all events belonging to the element.
        /// </summary>
        protected virtual EventHandlerList Events
        {
            get { return this._eventHandlers; }
        }

        /// <summary>
        /// Occurs when the element is loaded.
        /// </summary>
        public event EventHandler Load
        {
            add { this.Events.AddHandler(_loadEventKey, value); }
            remove { this.Events.RemoveHandler(_loadEventKey, value); }
        }

        /// <summary>
        /// Gets a collection of all child <see cref="SvgElement"/> objects.
        /// </summary>
        public virtual SvgElementCollection Children
        {
            get { return this._children; }
        }

        public IList<ISvgNode> Nodes
        {
            get { return this._nodes; }
        }

        public IEnumerable<SvgElement> Descendants()
        {
            return this.AsEnumerable().Descendants();
        }
        private IEnumerable<SvgElement> AsEnumerable()
        {
            yield return this;
        }

        /// <summary>
        /// Gets a value to determine whether the element has children.
        /// </summary>
        public virtual bool HasChildren()
        {
            return (this.Children.Count > 0);
        }

        /// <summary>
        /// Gets the parent <see cref="SvgElement"/>.
        /// </summary>
        /// <value>An <see cref="SvgElement"/> if one exists; otherwise null.</value>
        public virtual SvgElement Parent
        {
            get { return this._parent; }
        }

        public IEnumerable<SvgElement> Parents
        {
            get
            {
                var curr = this;
                while (curr.Parent != null)
                {
                    curr = curr.Parent;
                    yield return curr;
                }
            }
        }
        public IEnumerable<SvgElement> ParentsAndSelf
        {
            get
            {
                var curr = this;
                yield return curr;
                while (curr.Parent != null)
                {
                    curr = curr.Parent;
                    yield return curr;
                }
            }
        }

        /// <summary>
        /// Gets the owner <see cref="SvgDocument"/>.
        /// </summary>
        public virtual SvgDocument OwnerDocument
        {
            get
            {
                if (this is SvgDocument)
                {
                    return this as SvgDocument;
                }
                else
                {
                    if (this.Parent != null)
                        return Parent.OwnerDocument;
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// Gets a collection of element attributes.
        /// </summary>
        protected internal virtual SvgAttributeCollection Attributes
        {
            get
            {
                if (this._attributes == null)
                {
                    this._attributes = new SvgAttributeCollection(this);
                }

                return this._attributes;
            }
        }

        protected bool Writing { get; set; }

        protected internal TAttributeType GetAttribute<TAttributeType>(string attributeName, bool inherited, TAttributeType defaultValue = default(TAttributeType))
        {
            if (Writing)
                return Attributes.GetAttribute(attributeName, defaultValue);
            else
                return Attributes.GetInheritedAttribute(attributeName, inherited, defaultValue);
        }

        /// <summary>
        /// Gets a collection of custom attributes
        /// </summary>
        public SvgCustomAttributeCollection CustomAttributes
        {
            get { return this._customAttributes; }
        }

        /// <summary>
        /// Gets or sets the element transforms.
        /// </summary>
        /// <value>The transforms.</value>
        [SvgAttribute("transform")]
        public SvgTransformCollection Transforms
        {
            get { return GetAttribute<SvgTransformCollection>("transform", false); }
            set
            {
                var old = Transforms;
                if (old != null)
                    old.TransformChanged -= Attributes_AttributeChanged;
                value.TransformChanged += Attributes_AttributeChanged;
                Attributes["transform"] = value;
            }
        }

        /// <summary>
        /// Gets or sets the ID of the element.
        /// </summary>
        /// <exception cref="SvgException">The ID is already used within the <see cref="SvgDocument"/>.</exception>
        [SvgAttribute("id")]
        public string ID
        {
            get { return GetAttribute<string>("id", false); }
            set { SetAndForceUniqueID(value, false); }
        }

        /// <summary>
        /// Gets or sets the space handling.
        /// </summary>
        /// <value>The space handling.</value>
        [SvgAttribute("space", SvgAttributeAttribute.XmlNamespace)]
        public virtual XmlSpaceHandling SpaceHandling
        {
            get { return GetAttribute("space", true, XmlSpaceHandling.Inherit); }
            set { Attributes["space"] = value; }
        }

        public void SetAndForceUniqueID(string value, bool autoForceUniqueID = true, Action<SvgElement, string, string> logElementOldIDNewID = null)
        {
            // Don't do anything if it hasn't changed
            if (string.Compare(ID, value) == 0)
            {
                return;
            }

            if (OwnerDocument != null)
            {
                OwnerDocument.IdManager.Remove(this);
            }

            Attributes["id"] = value;

            if (OwnerDocument != null)
            {
                OwnerDocument.IdManager.AddAndForceUniqueID(this, null, autoForceUniqueID, logElementOldIDNewID);
            }
        }

        /// <summary>
        /// Only used by the ID Manager
        /// </summary>
        /// <param name="newID"></param>
        internal void ForceUniqueID(string newID)
        {
            Attributes["id"] = newID;
        }

        /// <summary>
        /// Called by the underlying <see cref="SvgElement"/> when an element has been added to the
        /// <see cref="Children"/> collection.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been added.</param>
        /// <param name="index">An <see cref="int"/> representing the index where the element was added to the collection.</param>
        protected virtual void AddElement(SvgElement child, int index)
        {
        }

        /// <summary>
        /// Fired when an Element was added to the children of this Element
        /// </summary>
        public event EventHandler<ChildAddedEventArgs> ChildAdded;

        /// <summary>
        /// Calls the <see cref="AddElement"/> method with the specified parameters.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been added.</param>
        /// <param name="index">An <see cref="int"/> representing the index where the element was added to the collection.</param>
        internal void OnElementAdded(SvgElement child, int index)
        {
            this.AddElement(child, index);
            SvgElement sibling = null;
            if (index < (Children.Count - 1))
            {
                sibling = Children[index + 1];
            }
            var handler = ChildAdded;
            if (handler != null)
            {
                handler(this, new ChildAddedEventArgs { NewChild = child, BeforeSibling = sibling });
            }
        }

        /// <summary>
        /// Called by the underlying <see cref="SvgElement"/> when an element has been removed from the
        /// <see cref="Children"/> collection.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been removed.</param>
        protected virtual void RemoveElement(SvgElement child)
        {
        }

        /// <summary>
        /// Calls the <see cref="RemoveElement"/> method with the specified <see cref="SvgElement"/> as the parameter.
        /// </summary>
        /// <param name="child">The <see cref="SvgElement"/> that has been removed.</param>
        internal void OnElementRemoved(SvgElement child)
        {
            this.RemoveElement(child);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SvgElement"/> class.
        /// </summary>
        public SvgElement()
        {
            this._children = new SvgElementCollection(this);
            this._eventHandlers = new EventHandlerList();
            this._elementName = string.Empty;
            this._customAttributes = new SvgCustomAttributeCollection(this);

            //subscribe to attribute events
            Attributes.AttributeChanged += Attributes_AttributeChanged;
            CustomAttributes.AttributeChanged += Attributes_AttributeChanged;
        }

        //dispatch attribute event
        void Attributes_AttributeChanged(object sender, AttributeEventArgs e)
        {
            OnAttributeChanged(e);
        }

        public virtual void InitialiseFromXML(XmlReader reader, SvgDocument document)
        {
            throw new NotImplementedException();
        }

        /// <summary>Derrived classes may decide that the element should not be written. For example, the text element shouldn't be written if it's empty.</summary>
        public virtual bool ShouldWriteElement()
        {
            //Write any element who has a name.
            return !string.IsNullOrEmpty(this.ElementName);
        }

        protected virtual void WriteStartElement(XmlWriter writer)
        {
            if (!string.IsNullOrEmpty(this.ElementName))
            {
                if (string.IsNullOrEmpty(this.ElementNamespace))
                    writer.WriteStartElement(this.ElementName);
                else
                {
                    var prefix = writer.LookupPrefix(this.ElementNamespace);
                    if (prefix == null && !this.ElementNamespace.Equals(SvgNamespaces.SvgNamespace))
                    {
                        foreach (var kvp in this.Namespaces)
                        {
                            if (kvp.Value.Equals(this.ElementNamespace) && !string.IsNullOrEmpty(kvp.Key))
                            {
                                prefix = kvp.Key;
                                break;
                            }
                        }
                    }
                    if (prefix == null)
                        writer.WriteStartElement(this.ElementName, this.ElementNamespace);
                    else
                        writer.WriteStartElement(prefix, this.ElementName, this.ElementNamespace);
                }
            }

            this.WriteAttributes(writer);
        }

        protected virtual void WriteEndElement(XmlWriter writer)
        {
            if (!string.IsNullOrEmpty(this.ElementName))
            {
                writer.WriteEndElement();
            }
        }

        protected virtual void WriteAttributes(XmlWriter writer)
        {
            // namespaces
            foreach (var ns in Namespaces)
            {
                if (ns.Value.Equals(SvgNamespaces.SvgNamespace) && !string.IsNullOrEmpty(ns.Key))
                    continue;
                writer.WriteAttributeString("xmlns", ns.Key, null, ns.Value);
            }

            // properties
            var styles = WritePropertyAttributes(writer);

            // events
            if (AutoPublishEvents)
            {
                foreach (var property in this.GetProperties().Where(x => x.DescriptorType == DescriptorType.Event))
                {
                    var evt = property.GetValue(this);

                    // if someone has registered publish the attribute
                    if (evt != null && !string.IsNullOrEmpty(this.ID))
                    {
                        string evtValue = this.ID + "/" + property.AttributeName;
                        WriteAttributeString(writer, property.AttributeName, null, evtValue);
                    }
                }
            }

            // add the custom attributes
            var additionalStyleValue = string.Empty;
            foreach (var item in this._customAttributes)
            {
                if (item.Key.Equals("style") && styles.Any())
                {
                    additionalStyleValue = item.Value;
                    continue;
                }
                var index = item.Key.LastIndexOf(":");
                if (index >= 0)
                {
                    var ns = item.Key.Substring(0, index);
                    var localName = item.Key.Substring(index + 1);
                    WriteAttributeString(writer, localName, ns, item.Value);
                }
                else
                    WriteAttributeString(writer, item.Key, null, item.Value);
            }

            // write the style property
            if (styles.Any())
            {
                var styleValues = styles.Select(s => s.Key + ":" + s.Value)
                    .Concat(Enumerable.Repeat(additionalStyleValue, 1));
                WriteAttributeString(writer, "style", null, string.Join(";", styleValues));
            }
        }

        private Dictionary<string, string> WritePropertyAttributes(XmlWriter writer)
        {
            var styles = _styles.ToDictionary(_styles => _styles.Key, _styles => _styles.Value.Last().Value);
            var opacityAttributes = new List<ISvgPropertyDescriptor>();
            var opacityValues = new Dictionary<string, float>();

            try
            {
                Writing = true;

                foreach (var property in this.GetProperties())
                {
                    if (property.Converter == null)
                    {
                        continue;
                    }
                    if (property.Converter.CanConvertTo(typeof(string)))
                    {
                        if (property.AttributeName == "fill-opacity" || property.AttributeName == "stroke-opacity")
                        {
                            opacityAttributes.Add(property);
                            continue;
                        }

                        if (Attributes.ContainsKey(property.AttributeName))
                        {
                            var propertyValue = property.GetValue(this);

                            var forceWrite = false;
                            var writeStyle = property.AttributeName == "fill" || property.AttributeName == "stroke";

                            if (Parent != null)
                            {
                                if (writeStyle && propertyValue == SvgPaintServer.NotSet)
                                    continue;

                                object parentValue;
                                if (TryResolveParentAttributeValue(property.AttributeName, out parentValue))
                                {
                                    if ((parentValue == propertyValue)
                                        || ((parentValue != null) && parentValue.Equals(propertyValue)))
                                    {
                                        if (writeStyle)
                                            continue;
                                    }
                                    else
                                        forceWrite = true;
                                }
                            }

                            var hasOpacity = writeStyle;
                            if (hasOpacity)
                            {
                                if (propertyValue is SvgColourServer && ((SvgColourServer)propertyValue).Colour.A < 255)
                                {
                                    var opacity = ((SvgColourServer)propertyValue).Colour.A / 255f;
                                    opacityValues.Add(property.AttributeName + "-opacity", opacity);
                                }
                            }
                            // dotnetcore throws exception if input is null
                            var value = propertyValue == null ? null : (string)property.Converter.ConvertTo(propertyValue, typeof(string));

                            if (propertyValue != null)
                            {
                                //Only write the attribute's value if it is not the default value, not null/empty, or we're forcing the write.
                                if (forceWrite || !string.IsNullOrEmpty(value))
                                {
                                    if (writeStyle)
                                    {
                                        styles[property.AttributeName] = value;
                                    }
                                    else
                                    {
                                        WriteAttributeString(writer, property.AttributeName, property.AttributeNamespace, value);
                                    }
                                }
                            }
                            else if (property.AttributeName == "fill") //if fill equals null, write 'none'
                            {
                                if (writeStyle)
                                {
                                    styles[property.AttributeName] = value;
                                }
                                else
                                {
                                    WriteAttributeString(writer, property.AttributeName, property.AttributeNamespace, value);
                                }
                            }
                        }
                    }
                }

                foreach (var property in opacityAttributes)
                {
                    var opacity = 1f;
                    var write = false;

                    var key = property.AttributeName;
                    if (opacityValues.ContainsKey(key))
                    {
                        opacity = opacityValues[key];
                        write = true;
                    }
                    if (Attributes.ContainsKey(key))
                    {
                        opacity *= (float)property.GetValue(this);
                        write = true;
                    }
                    if (write)
                    {
                        opacity = (float)Math.Round(opacity, 2, MidpointRounding.AwayFromZero);
                        var value = (string)property.Converter.ConvertTo(opacity, typeof(string));
                        if (!string.IsNullOrEmpty(value))
                            WriteAttributeString(writer, property.AttributeName, property.AttributeNamespace, value);
                    }
                }
            }
            finally
            {
                Writing = false;
            }
            return styles;
        }

        private void WriteAttributeString(XmlWriter writer, string name, string ns, string value)
        {
            if (string.IsNullOrEmpty(ns))
                writer.WriteAttributeString(name, value);
            else
            {
                var prefix = writer.LookupPrefix(ns);
                if (prefix != null)
                    ns = null;
                writer.WriteAttributeString(prefix, name, ns, value);
            }
        }

        public bool AutoPublishEvents = true;

        private bool TryResolveParentAttributeValue(string attributeKey, out object parentAttributeValue)
        {
            parentAttributeValue = null;

            //attributeKey = char.ToUpper(attributeKey[0]) + attributeKey.Substring(1);

            var currentParent = Parent;
            var resolved = false;
            while (currentParent != null)
            {
                if (currentParent.Attributes.ContainsKey(attributeKey))
                {
                    resolved = true;
                    parentAttributeValue = currentParent.Attributes[attributeKey];
                    if (parentAttributeValue != null)
                        break;
                }
                currentParent = currentParent.Parent;
            }

            return resolved;
        }

        /// <summary>
        /// Write this SvgElement out using a given XmlWriter.
        /// </summary>
        /// <param name="writer">The XmlWriter to use.</param>
        /// <remarks>
        /// Recommendation is to create an XmlWriter by calling a factory method,<br/>
        /// e.g. <see cref="XmlWriter.Create(System.IO.Stream)"/>,
        /// as per <a href="https://docs.microsoft.com/dotnet/api/system.xml.xmltextwriter#remarks">Microsoft documentation</a>.<br/>
        /// <br/>
        /// However, unlike an <see cref="XmlTextWriter"/> created via 'new XmlTextWriter()',<br/>
        /// a factory-constructed XmlWriter will not flush output until it is closed<br/>
        /// (normally via a using statement), or unless the client explicitly calls <see cref="XmlWriter.Flush()"/>.
        /// </remarks>
        public virtual void Write(XmlWriter writer)
        {
            if (ShouldWriteElement())
            {
                this.WriteStartElement(writer);
                this.WriteChildren(writer);
                this.WriteEndElement(writer);
            }
        }

        protected virtual void WriteChildren(XmlWriter writer)
        {
            if (this.Nodes.Any())
            {
                SvgContentNode content;
                foreach (var node in this.Nodes)
                {
                    content = node as SvgContentNode;
                    if (content == null)
                    {
                        ((SvgElement)node).Write(writer);
                    }
                    else if (!string.IsNullOrEmpty(content.Content))
                    {
                        writer.WriteString(content.Content);
                    }
                }
            }
            else
            {
                //write the content
                if (!String.IsNullOrEmpty(this.Content))
                    writer.WriteString(this.Content);

                //write all children
                foreach (SvgElement child in this.Children)
                {
                    child.Write(writer);
                }
            }
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public virtual object Clone()
        {
            return DeepCopy();
        }

        public abstract SvgElement DeepCopy();

        ISvgNode ISvgNode.DeepCopy()
        {
            return DeepCopy();
        }

        public virtual SvgElement DeepCopy<T>() where T : SvgElement, new()
        {
            var newObj = new T
            {
                Content = Content,
                ElementName = ElementName
            };

            //if (this.Parent != null)
            //    this.Parent.Children.Add(newObj);

            foreach (var attribute in Attributes)
            {
                var value = attribute.Value is ICloneable ? ((ICloneable)attribute.Value).Clone() : attribute.Value;
                newObj.Attributes.Add(attribute.Key, value);
            }

            foreach (var child in Children)
                newObj.Children.Add(child.DeepCopy());

            foreach (var property in this.GetProperties().Where(x => x.DescriptorType == DescriptorType.Event))
            {
                var evt = property.GetValue(this);

                // if someone has registered also register here
                if (evt != null)
                {
                    if (property.AttributeName == "MouseDown")
                        newObj.MouseDown += delegate { };
                    else if (property.AttributeName == "MouseUp")
                        newObj.MouseUp += delegate { };
                    else if (property.AttributeName == "MouseOver")
                        newObj.MouseOver += delegate { };
                    else if (property.AttributeName == "MouseOut")
                        newObj.MouseOut += delegate { };
                    else if (property.AttributeName == "MouseMove")
                        newObj.MouseMove += delegate { };
                    else if (property.AttributeName == "MouseScroll")
                        newObj.MouseScroll += delegate { };
                    else if (property.AttributeName == "Click")
                        newObj.Click += delegate { };
                    else if (property.AttributeName == "Change") // text element
                        (newObj as SvgText).Change += delegate { };
                }
            }
            foreach (var attribute in CustomAttributes)
                newObj.CustomAttributes.Add(attribute.Key, attribute.Value);

            foreach (var node in Nodes)
            {
                if (node is SvgElement)
                {
                    var index = Children.IndexOf((SvgElement)node);
                    if (index >= 0)
                    {
                        newObj.Nodes.Add(newObj.Children[index]);
                        continue;
                    }
                }
                newObj.Nodes.Add(node.DeepCopy());
            }

            foreach (var style in _styles)
                foreach (var pair in style.Value)
                    newObj.AddStyle(style.Key, pair.Value, pair.Key);

            return newObj;
        }

        /// <summary>
        /// Fired when an Atrribute of this Element has changed
        /// </summary>
        public event EventHandler<AttributeEventArgs> AttributeChanged;

        protected void OnAttributeChanged(AttributeEventArgs args)
        {
            var handler = AttributeChanged;
            if (handler != null)
            {
                handler(this, args);
            }
        }

        /// <summary>
        /// Fired when an Atrribute of this Element has changed
        /// </summary>
        public event EventHandler<ContentEventArgs> ContentChanged;

        protected void OnContentChanged(ContentEventArgs args)
        {
            var handler = ContentChanged;
            if (handler != null)
            {
                handler(this, args);
            }
        }

        #region graphical EVENTS

        /*
            onfocusin = "<anything>"
            onfocusout = "<anything>"
            onactivate = "<anything>"
            onclick = "<anything>"
            onmousedown = "<anything>"
            onmouseup = "<anything>"
            onmouseover = "<anything>"
            onmousemove = "<anything>"
            onmouseout = "<anything>"
         */

        [SvgAttribute("onclick")]
        public event EventHandler<MouseArg> Click;

        [SvgAttribute("onmousedown")]
        public event EventHandler<MouseArg> MouseDown;

        [SvgAttribute("onmouseup")]
        public event EventHandler<MouseArg> MouseUp;

        [SvgAttribute("onmousemove")]
        public event EventHandler<MouseArg> MouseMove;

        [SvgAttribute("onmousescroll")]
        public event EventHandler<MouseScrollArg> MouseScroll;

        [SvgAttribute("onmouseover")]
        public event EventHandler<MouseArg> MouseOver;

        [SvgAttribute("onmouseout")]
        public event EventHandler<MouseArg> MouseOut;

        //click
        protected void RaiseMouseClick(object sender, MouseArg e)
        {
            var handler = Click;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        //down
        protected void RaiseMouseDown(object sender, MouseArg e)
        {
            var handler = MouseDown;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        //up
        protected void RaiseMouseUp(object sender, MouseArg e)
        {
            var handler = MouseUp;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        protected void RaiseMouseMove(object sender, MouseArg e)
        {
            var handler = MouseMove;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        //over
        protected void RaiseMouseOver(object sender, MouseArg args)
        {
            var handler = MouseOver;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        //out
        protected void RaiseMouseOut(object sender, MouseArg args)
        {
            var handler = MouseOut;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        //scroll
        protected void OnMouseScroll(int scroll, bool ctrlKey, bool shiftKey, bool altKey, string sessionID)
        {
            RaiseMouseScroll(this, new MouseScrollArg { Scroll = scroll, AltKey = altKey, ShiftKey = shiftKey, CtrlKey = ctrlKey, SessionID = sessionID });
        }

        protected void RaiseMouseScroll(object sender, MouseScrollArg e)
        {
            var handler = MouseScroll;
            if (handler != null)
            {
                handler(sender, e);
            }
        }

        #endregion graphical EVENTS
    }

    public class SVGArg : EventArgs
    {
        public string SessionID;
    }

    /// <summary>
    /// Describes the Attribute which was set
    /// </summary>
    public class AttributeEventArgs : SVGArg
    {
        public string Attribute;
        public object Value;
    }

    /// <summary>
    /// Content of this whas was set
    /// </summary>
    public class ContentEventArgs : SVGArg
    {
        public string Content;
    }

    /// <summary>
    /// Describes the Attribute which was set
    /// </summary>
    public class ChildAddedEventArgs : SVGArg
    {
        public SvgElement NewChild;
        public SvgElement BeforeSibling;
    }

    /// <summary>
    /// Represents the state of the mouse at the moment the event occured.
    /// </summary>
    public class MouseArg : SVGArg
    {
        public float x;
        public float y;

        /// <summary>
        /// 1 = left, 2 = middle, 3 = right
        /// </summary>
        public int Button;

        /// <summary>
        /// Amount of mouse clicks, e.g. 2 for double click
        /// </summary>
        public int ClickCount = -1;

        /// <summary>
        /// Alt modifier key pressed
        /// </summary>
        public bool AltKey;

        /// <summary>
        /// Shift modifier key pressed
        /// </summary>
        public bool ShiftKey;

        /// <summary>
        /// Control modifier key pressed
        /// </summary>
        public bool CtrlKey;
    }

    /// <summary>
    /// Represents a string argument
    /// </summary>
    public class StringArg : SVGArg
    {
        public string s;
    }

    public class MouseScrollArg : SVGArg
    {
        public int Scroll;

        /// <summary>
        /// Alt modifier key pressed
        /// </summary>
        public bool AltKey;

        /// <summary>
        /// Shift modifier key pressed
        /// </summary>
        public bool ShiftKey;

        /// <summary>
        /// Control modifier key pressed
        /// </summary>
        public bool CtrlKey;
    }

    public interface ISvgNode
    {
        string Content { get; }

        /// <summary>
        /// Create a deep copy of this <see cref="ISvgNode"/>.
        /// </summary>
        /// <returns>A deep copy of this <see cref="ISvgNode"/></returns>
        ISvgNode DeepCopy();
    }

    /// <summary>This interface mostly indicates that a node is not to be drawn when rendering the SVG.</summary>
    public interface ISvgDescriptiveElement
    {
    }

    internal interface ISvgElement
    {
        SvgElement Parent { get; }
        SvgElementCollection Children { get; }
        IList<ISvgNode> Nodes { get; }

#if !NO_SDC
        void Render(ISvgRenderer renderer);
#endif
    }
}
