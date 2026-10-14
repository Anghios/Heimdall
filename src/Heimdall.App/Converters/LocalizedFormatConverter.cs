/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Globalization;
using System.Windows.Data;

namespace Heimdall.App.Converters;

/// <summary>
/// Fills a localized format with bound values, for any text that is not a grid row's name:
/// a labelled value's accessible name, a tooltip that quotes the value it describes.
/// </summary>
/// <remarks>
/// The first value is the format, bound from <c>LocalizationSource.Instance</c> so a locale
/// change re-evaluates the binding; every later value fills one of its slots. It formats exactly
/// as <see cref="AccessibleRowNameConverter"/> does, through the same code, and exists apart from
/// it because that converter's name is a promise the row-name guard holds it to: an
/// <c>A11y*Row</c> key.
/// </remarks>
public sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => AccessibleRowNameConverter.Format(values, culture);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        var values = new object[targetTypes.Length];
        Array.Fill(values, System.Windows.Data.Binding.DoNothing);
        return values;
    }
}
