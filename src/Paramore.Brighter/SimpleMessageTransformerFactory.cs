﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;

namespace Paramore.Brighter
{
    /// <summary>
    /// Class SimpleMessageTransformerFactory.
    /// This class allows you to return a simple function that finds a transformer. Intended for lightweight transformer pipelines.
    /// We recommend you wrap your IoC container for heavyweight mapping.
    /// </summary>
    public class SimpleMessageTransformerFactory : IAmAMessageTransformerFactory
    {
        private readonly Func<Type, IAmAMessageTransformAsync> _factoryMethod;

        public SimpleMessageTransformerFactory(Func<Type, IAmAMessageTransformAsync> factoryMethod)
        {
            _factoryMethod = factoryMethod;
        }

        public IAmAMessageTransformAsync Create(Type transformerType)
        {
            return _factoryMethod(transformerType);
        }

        public void Release(IAmAMessageTransformAsync transformer)
        {
            return;
        }
    }
}
