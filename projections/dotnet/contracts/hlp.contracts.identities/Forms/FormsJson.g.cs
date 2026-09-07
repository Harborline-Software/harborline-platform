#nullable enable
#pragma warning disable CS1591 // Generated wire infrastructure is documented by the module interface.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;

namespace Harborline.Contracts.Forms;

[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    private readonly T? _value;

    private Optional(T? value)
    {
        HasValue = true;
        _value = value;
    }

    public bool HasValue { get; }
    public T? Value => HasValue ? _value : throw new InvalidOperationException("The optional value is absent.");
    public static Optional<T> Some(T? value) => new(value);
    public static implicit operator Optional<T>(T? value) => Some(value);
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Optional<T>.Some(JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (!value.HasValue) throw new JsonException("An absent Optional value must be omitted by its containing property.");
        JsonSerializer.Serialize(writer, value.Value, options);
    }
}

public sealed class ClosedEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> FromWire = typeof(TEnum)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name, field => (TEnum)field.GetValue(null)!);
    private static readonly IReadOnlyDictionary<TEnum, string> ToWire = FromWire.ToDictionary(row => row.Value, row => row.Key);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !FromWire.TryGetValue(reader.GetString()!, out var value))
            throw new JsonException($"Unknown closed {typeof(TEnum).Name} value.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!ToWire.TryGetValue(value, out var wire)) throw new JsonException($"Unknown closed {typeof(TEnum).Name} value.");
        writer.WriteStringValue(wire);
    }
}

public sealed class ControlHintJsonConverter : JsonConverter<ControlHint>
{
    public override ControlHint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? new(reader.GetString()!) : throw new JsonException("ControlHint must be a string.");
    public override void Write(Utf8JsonWriter writer, ControlHint value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class LayoutGapJsonConverter : JsonConverter<LayoutGap>
{
    public override LayoutGap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && LayoutGap.AllowedValues.Contains(value)
            ? new(value) : throw new JsonException("Unknown closed LayoutGap value.");
    public override void Write(Utf8JsonWriter writer, LayoutGap value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
}

public sealed class FormsWireException(string code, string type, string? detail = null)
    : JsonException($"{code}: {type}{(detail is null ? string.Empty : $" ({detail})")}")
{
    public string Code { get; } = code;
    public string WireType { get; } = type;
    public string? Detail { get; } = detail;
}

public static class FormsJson
{
    private const string ModelJson = """
{
  "schemaVersion": 1,
  "declarations": {
    "InternationalizedText": {
      "kind": "object",
      "properties": {
        "defaultLocale": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "values": {
          "optional": false,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "string"
            }
          }
        }
      }
    },
    "FormDefinitionStatus": {
      "kind": "enum",
      "values": [
        "Draft",
        "Published",
        "Deprecated",
        "Withdrawn"
      ]
    },
    "PiiSensitivity": {
      "kind": "enum",
      "values": [
        "None",
        "Sensitive"
      ]
    },
    "RuleTier": {
      "kind": "enum",
      "values": [
        "JsonSchema",
        "JsonLogic",
        "PowerFx"
      ]
    },
    "RuleScope": {
      "kind": "enum",
      "values": [
        "Field",
        "Section",
        "Schema",
        "Row",
        "Table"
      ]
    },
    "RuleActionKind": {
      "kind": "enum",
      "values": [
        "Visibility",
        "Required",
        "ReadOnly",
        "Validate",
        "Compute",
        "Presentation",
        "Options"
      ]
    },
    "PresentationHint": {
      "kind": "object",
      "properties": {
        "severity": {
          "optional": true,
          "shape": {
            "kind": "union",
            "members": [
              {
                "kind": "literal",
                "value": "info"
              },
              {
                "kind": "literal",
                "value": "warn"
              },
              {
                "kind": "literal",
                "value": "error"
              }
            ]
          }
        },
        "badge": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "styleToken": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "ControlHint": {
      "kind": "openString",
      "documentedValues": [
        "text",
        "textarea",
        "date",
        "datetime",
        "address",
        "geo-point",
        "taxonomy-coding",
        "attachment",
        "reference",
        "variant",
        "recurrence-rule"
      ]
    },
    "SectionLayoutKind": {
      "kind": "enum",
      "values": [
        "stack",
        "flex",
        "grid"
      ]
    },
    "FlexDirection": {
      "kind": "enum",
      "values": [
        "row",
        "column"
      ]
    },
    "FlexWrap": {
      "kind": "enum",
      "values": [
        "nowrap",
        "wrap"
      ]
    },
    "LayoutGap": {
      "kind": "enum",
      "values": [
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        8
      ]
    },
    "LayoutBreakpoint": {
      "kind": "enum",
      "values": [
        "sm",
        "md",
        "lg"
      ]
    },
    "LayoutDensity": {
      "kind": "enum",
      "values": [
        "comfortable",
        "compact"
      ]
    },
    "LayoutAlign": {
      "kind": "enum",
      "values": [
        "start",
        "center",
        "end",
        "stretch"
      ]
    },
    "FieldWidth": {
      "kind": "enum",
      "values": [
        "auto",
        "1/4",
        "1/3",
        "1/2",
        "2/3",
        "3/4",
        "full"
      ]
    },
    "SectionLayout": {
      "kind": "object",
      "properties": {
        "kind": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "SectionLayoutKind"
          }
        },
        "direction": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FlexDirection"
          }
        },
        "wrap": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FlexWrap"
          }
        },
        "columns": {
          "optional": true,
          "shape": {
            "kind": "number"
          }
        },
        "gap": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutGap"
          }
        },
        "collapseBelow": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutBreakpoint"
          }
        },
        "density": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutDensity"
          }
        },
        "align": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutAlign"
          }
        }
      }
    },
    "FieldPlacement": {
      "kind": "object",
      "properties": {
        "colSpan": {
          "optional": true,
          "shape": {
            "kind": "number"
          }
        },
        "grow": {
          "optional": true,
          "shape": {
            "kind": "number"
          }
        },
        "width": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FieldWidth"
          }
        },
        "align": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutAlign"
          }
        }
      }
    },
    "SectionAccess": {
      "kind": "object",
      "properties": {
        "readRoles": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "writeRoles": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "readConditionExpression": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "FormItemKind": {
      "kind": "enum",
      "values": [
        "field",
        "group",
        "collection",
        "reference",
        "content",
        "action"
      ]
    },
    "ContentNode": {
      "kind": "discriminatedUnion",
      "discriminator": "kind",
      "variants": {
        "heading": {
          "kind": "object",
          "properties": {
            "text": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            },
            "level": {
              "optional": true,
              "shape": {
                "kind": "number"
              }
            }
          }
        },
        "paragraph": {
          "kind": "object",
          "properties": {
            "text": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            }
          }
        }
      }
    },
    "FormActionKind": {
      "kind": "enum",
      "values": [
        "open-url",
        "scroll-to-section"
      ]
    },
    "FormActionConfig": {
      "kind": "object",
      "properties": {
        "kind": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "FormActionKind"
          }
        },
        "label": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "url": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "sectionId": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "ReusableUnitVersionSelector": {
      "kind": "object",
      "properties": {
        "pinnedVersion": {
          "optional": false,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "string"
            }
          }
        }
      }
    },
    "ReusableUnitRef": {
      "kind": "object",
      "properties": {
        "unitId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "version": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "ReusableUnitVersionSelector"
          }
        }
      }
    },
    "Cardinality": {
      "kind": "object",
      "properties": {
        "min": {
          "optional": false,
          "shape": {
            "kind": "number"
          }
        },
        "max": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "number"
            }
          }
        }
      }
    },
    "CollectionColumn": {
      "kind": "object",
      "properties": {
        "width": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FieldWidth"
          }
        },
        "align": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LayoutAlign"
          }
        }
      }
    },
    "CollectionTableConfig": {
      "kind": "object",
      "properties": {
        "columns": {
          "optional": true,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "ref",
              "name": "CollectionColumn"
            }
          }
        },
        "totals": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        }
      }
    },
    "FormItem": {
      "kind": "discriminatedUnion",
      "discriminator": "kind",
      "variants": {
        "field": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            }
          }
        },
        "group": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "title": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            },
            "items": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "FormItem"
                }
              }
            },
            "layout": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "SectionLayout"
              }
            },
            "placement": {
              "optional": true,
              "shape": {
                "kind": "record",
                "value": {
                  "kind": "ref",
                  "name": "FieldPlacement"
                }
              }
            },
            "aspects": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "AspectOverlay"
              }
            }
          }
        },
        "collection": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "title": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            },
            "items": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "FormItem"
                }
              }
            },
            "cardinality": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "Cardinality"
              }
            },
            "table": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "CollectionTableConfig"
              }
            },
            "aspects": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "AspectOverlay"
              }
            }
          }
        },
        "reference": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "reference": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "ReusableUnitRef"
              }
            }
          }
        },
        "content": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "content": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "ContentNode"
                }
              }
            }
          }
        },
        "action": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "action": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "FormActionConfig"
              }
            }
          }
        }
      }
    },
    "FormSection": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "title": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "fields": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "access": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "SectionAccess"
          }
        },
        "layout": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "SectionLayout"
          }
        },
        "fieldPlacement": {
          "optional": true,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "ref",
              "name": "FieldPlacement"
            }
          }
        },
        "aspects": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "AspectOverlay"
          }
        },
        "items": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormItem"
            }
          }
        }
      }
    },
    "FieldConfig": {
      "kind": "object",
      "properties": {
        "currencyCode": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "accept": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "multiple": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        }
      }
    },
    "FieldOverlay": {
      "kind": "object",
      "properties": {
        "label": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "helpText": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "controlHint": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "ControlHint"
          }
        },
        "piiSensitivity": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "PiiSensitivity"
          }
        },
        "fieldReadRoles": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "fieldWriteRoles": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "aspects": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "AspectOverlay"
          }
        },
        "config": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FieldConfig"
          }
        }
      }
    },
    "RuleDefinition": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "tier": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "RuleTier"
          }
        },
        "scope": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "RuleScope"
          }
        },
        "scopeTarget": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "expression": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "action": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "RuleActionKind"
          }
        },
        "errorMessage": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "presentation": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "PresentationHint"
          }
        }
      }
    },
    "OutputType": {
      "kind": "enum",
      "values": [
        "Value",
        "Validity",
        "Visibility",
        "Presentation",
        "Options"
      ]
    },
    "ValueState": {
      "kind": "enum",
      "values": [
        "Resolved",
        "Error",
        "Pending"
      ]
    },
    "Severity": {
      "kind": "enum",
      "values": [
        "info",
        "warn",
        "error"
      ]
    },
    "RuleError": {
      "kind": "object",
      "properties": {
        "code": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "params": {
          "optional": false,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "string"
            }
          }
        }
      }
    },
    "ComputedValue": {
      "kind": "object",
      "properties": {
        "state": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "ValueState"
          }
        },
        "value": {
          "optional": true,
          "shape": {
            "kind": "unknown"
          }
        },
        "error": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "RuleError"
          }
        }
      }
    },
    "Validity": {
      "kind": "object",
      "properties": {
        "ok": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "error": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "RuleError"
          }
        }
      }
    },
    "VisibilityState": {
      "kind": "object",
      "properties": {
        "visible": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "required": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "readOnly": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        }
      }
    },
    "PresentationOutcome": {
      "kind": "object",
      "properties": {
        "severity": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "ref",
              "name": "Severity"
            }
          }
        },
        "badge": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "styleToken": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "OptionsOutcome": {
      "kind": "object",
      "properties": {
        "state": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "ValueState"
          }
        },
        "options": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "unknown"
            }
          }
        },
        "error": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "RuleError"
          }
        }
      }
    },
    "RuleOutcome": {
      "kind": "object",
      "properties": {
        "ruleId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "target": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "outputType": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "OutputType"
          }
        },
        "value": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "ComputedValue"
          }
        },
        "validity": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "Validity"
          }
        },
        "visibility": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "VisibilityState"
          }
        },
        "presentation": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "PresentationOutcome"
          }
        },
        "options": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "OptionsOutcome"
          }
        }
      }
    },
    "OnSuccessConfig": {
      "kind": "object",
      "properties": {
        "redirectUrl": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "hostCallback": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "WizardSettings": {
      "kind": "object",
      "properties": {
        "review": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "confirmation": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "confirmationMessage": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "onSuccess": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "OnSuccessConfig"
          }
        }
      }
    },
    "FormPage": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "title": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "sections": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "visibleWhen": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "checks": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        }
      }
    },
    "AsyncValidationCheck": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "connector": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "field": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "inputs": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "failCode": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "debounceMs": {
          "optional": true,
          "shape": {
            "kind": "number"
          }
        },
        "allowsSensitiveInputs": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        }
      }
    },
    "HarborlineOverlay": {
      "kind": "object",
      "properties": {
        "fields": {
          "optional": false,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "ref",
              "name": "FieldOverlay"
            }
          }
        },
        "sections": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormSection"
            }
          }
        },
        "rules": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "RuleDefinition"
            }
          }
        },
        "title": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "description": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "aspects": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "AspectOverlay"
          }
        },
        "pages": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormPage"
            }
          }
        },
        "wizard": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "WizardSettings"
          }
        },
        "asyncChecks": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "AsyncValidationCheck"
            }
          }
        }
      }
    },
    "FormDefinitionLineage": {
      "kind": "object",
      "properties": {
        "parentDefinitionId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "parentVersion": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "IdentityRef": {
      "kind": "object",
      "properties": {
        "scheme": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "value": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "FormFieldValidationCode": {
      "kind": "enum",
      "values": [
        "required",
        "minLength",
        "maxLength",
        "pattern",
        "minimum",
        "maximum"
      ]
    },
    "FormFieldValidation": {
      "kind": "object",
      "properties": {
        "code": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "FormFieldValidationCode"
          }
        },
        "param": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "FormFieldAuthoringMetadata": {
      "kind": "object",
      "properties": {
        "type": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "required": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "validations": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormFieldValidation"
            }
          }
        },
        "options": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        }
      }
    },
    "FormDefinition": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "version": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "status": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "FormDefinitionStatus"
          }
        },
        "tenant": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "owner": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "IdentityRef"
          }
        },
        "schemaRef": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "overlay": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "HarborlineOverlay"
          }
        },
        "lineage": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FormDefinitionLineage"
          }
        },
        "createdAt": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "updatedAt": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "fieldsMeta": {
          "optional": true,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "ref",
              "name": "FormFieldAuthoringMetadata"
            }
          }
        }
      }
    },
    "FormViewFieldRules": {
      "kind": "object",
      "properties": {
        "visible": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "required": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "readOnly": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "computed": {
          "optional": true,
          "shape": {
            "kind": "unknown"
          }
        },
        "presentationSeverity": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "ref",
              "name": "Severity"
            }
          }
        },
        "presentationBadge": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "presentationStyleToken": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "FormViewField": {
      "kind": "object",
      "properties": {
        "name": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "label": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "helpText": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "ref",
              "name": "InternationalizedText"
            }
          }
        },
        "controlHint": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "string"
            }
          }
        },
        "isSensitive": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "isReadable": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "value": {
          "optional": true,
          "shape": {
            "kind": "unknown"
          }
        },
        "readOnly": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "presentation": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "PresentationOutcome"
          }
        },
        "rules": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "FormViewFieldRules"
          }
        }
      }
    },
    "FormViewItem": {
      "kind": "discriminatedUnion",
      "discriminator": "kind",
      "variants": {
        "field": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "field": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "FormViewField"
              }
            }
          }
        },
        "group": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "title": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            },
            "items": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "FormViewItem"
                }
              }
            },
            "layout": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "SectionLayout"
              }
            },
            "placement": {
              "optional": true,
              "shape": {
                "kind": "record",
                "value": {
                  "kind": "ref",
                  "name": "FieldPlacement"
                }
              }
            }
          }
        },
        "collection": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "title": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "InternationalizedText"
              }
            },
            "items": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "FormViewItem"
                }
              }
            },
            "cardinality": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "Cardinality"
              }
            },
            "table": {
              "optional": true,
              "shape": {
                "kind": "ref",
                "name": "CollectionTableConfig"
              }
            }
          }
        },
        "content": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "content": {
              "optional": false,
              "shape": {
                "kind": "array",
                "element": {
                  "kind": "ref",
                  "name": "ContentNode"
                }
              }
            }
          }
        },
        "action": {
          "kind": "object",
          "properties": {
            "key": {
              "optional": false,
              "shape": {
                "kind": "string"
              }
            },
            "action": {
              "optional": false,
              "shape": {
                "kind": "ref",
                "name": "FormActionConfig"
              }
            }
          }
        }
      }
    },
    "FormViewSection": {
      "kind": "object",
      "properties": {
        "id": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "title": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "InternationalizedText"
          }
        },
        "fields": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormViewField"
            }
          }
        },
        "layout": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "SectionLayout"
          }
        },
        "fieldPlacement": {
          "optional": true,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "ref",
              "name": "FieldPlacement"
            }
          }
        },
        "items": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormViewItem"
            }
          }
        }
      }
    },
    "FormView": {
      "kind": "object",
      "properties": {
        "formId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "version": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "title": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "ref",
              "name": "InternationalizedText"
            }
          }
        },
        "description": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "ref",
              "name": "InternationalizedText"
            }
          }
        },
        "sections": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "FormViewSection"
            }
          }
        }
      }
    },
    "ValidationErrorKind": {
      "kind": "enum",
      "values": [
        "Schema",
        "ResourceBound",
        "Authorization",
        "NotFound"
      ]
    },
    "ValidationError": {
      "kind": "object",
      "properties": {
        "jsonPointer": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "message": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "kind": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "ValidationErrorKind"
          }
        },
        "code": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        },
        "params": {
          "optional": true,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "string"
            }
          }
        }
      }
    },
    "ValidationResult": {
      "kind": "object",
      "properties": {
        "isValid": {
          "optional": false,
          "shape": {
            "kind": "boolean"
          }
        },
        "errors": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "ValidationError"
            }
          }
        }
      }
    },
    "FormSubmitResponse": {
      "kind": "object",
      "properties": {
        "instanceId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "HARBORLINE_JSONLOGIC_V1": {
      "kind": "constant",
      "value": "harborline-jsonlogic/v1"
    },
    "SubmissionBindingHeader": {
      "kind": "object",
      "properties": {
        "schemaRef": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "definitionId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "definitionVersion": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "engineVersion": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "localeChain": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "submittedAt": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "SnapshotCaptureMode": {
      "kind": "enum",
      "values": [
        "none",
        "full-projection",
        "signed-dtbs-hash"
      ]
    },
    "SignedDtbsHash": {
      "kind": "object",
      "properties": {
        "hashAlgorithm": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "dtbsHash": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "signatureAlgorithm": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "signature": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "publicKeyRef": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "SubmissionSnapshot": {
      "kind": "object",
      "properties": {
        "mode": {
          "optional": false,
          "shape": {
            "kind": "exclude",
            "source": {
              "kind": "ref",
              "name": "SnapshotCaptureMode"
            },
            "excluded": {
              "kind": "literal",
              "value": "none"
            }
          }
        },
        "capturedAt": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "projection": {
          "optional": true,
          "shape": {
            "kind": "unknown"
          }
        },
        "signed": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "SignedDtbsHash"
          }
        }
      }
    },
    "SubmissionMintAuditPayload": {
      "kind": "object",
      "properties": {
        "op": {
          "optional": false,
          "shape": {
            "kind": "literal",
            "value": "form-instance-mint"
          }
        },
        "form": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "version": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "encryptedFields": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "binding": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "SubmissionBindingHeader"
          }
        },
        "snapshot": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "SubmissionSnapshot"
          }
        }
      }
    },
    "DraftSaveRequest": {
      "kind": "object",
      "properties": {
        "values": {
          "optional": false,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "unknown"
            }
          }
        },
        "subjectId": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "DraftSavedResponse": {
      "kind": "object",
      "properties": {
        "caseId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "tenantId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "partyId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "DraftView": {
      "kind": "object",
      "properties": {
        "caseId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "formId": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "values": {
          "optional": false,
          "shape": {
            "kind": "record",
            "value": {
              "kind": "unknown"
            }
          }
        },
        "subjectId": {
          "optional": true,
          "shape": {
            "kind": "nullable",
            "value": {
              "kind": "string"
            }
          }
        },
        "updatedAt": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "Immutability": {
      "kind": "enum",
      "values": [
        "Mutable",
        "AppendOnly",
        "WriteOnce"
      ]
    },
    "ProvenanceKind": {
      "kind": "enum",
      "values": [
        "Stored",
        "Computed",
        "Imported"
      ]
    },
    "MeasureRole": {
      "kind": "enum",
      "values": [
        "None",
        "Measure",
        "Dimension"
      ]
    },
    "Tag": {
      "kind": "object",
      "properties": {
        "system": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "code": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "display": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "ClassificationAspect": {
      "kind": "object",
      "properties": {
        "tags": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "ref",
              "name": "Tag"
            }
          }
        }
      }
    },
    "AccessAspect": {
      "kind": "object",
      "properties": {
        "readRoles": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "writeRoles": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "object",
              "properties": {
                "vocabulary": {
                  "optional": false,
                  "shape": {
                    "kind": "enum",
                    "values": [
                      "sys.platform-roles",
                      "tax.roles"
                    ]
                  }
                },
                "name": {
                  "optional": false,
                  "shape": {
                    "kind": "string"
                  }
                }
              }
            }
          }
        },
        "readConditionExpression": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "RetentionRequirement": {
      "kind": "object",
      "properties": {
        "regime": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "floorClass": {
          "optional": false,
          "shape": {
            "kind": "string"
          }
        },
        "minimumRetentionDays": {
          "optional": false,
          "shape": {
            "kind": "number"
          }
        }
      }
    },
    "ResidencyRequirement": {
      "kind": "object",
      "properties": {
        "allowedJurisdictions": {
          "optional": false,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        },
        "prohibitedJurisdictions": {
          "optional": true,
          "shape": {
            "kind": "array",
            "element": {
              "kind": "string"
            }
          }
        }
      }
    },
    "Provenance": {
      "kind": "object",
      "properties": {
        "kind": {
          "optional": false,
          "shape": {
            "kind": "ref",
            "name": "ProvenanceKind"
          }
        },
        "source": {
          "optional": true,
          "shape": {
            "kind": "string"
          }
        }
      }
    },
    "LifecycleAspect": {
      "kind": "object",
      "properties": {
        "retention": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "RetentionRequirement"
          }
        },
        "residency": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "ResidencyRequirement"
          }
        },
        "immutability": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "Immutability"
          }
        },
        "provenance": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "Provenance"
          }
        }
      }
    },
    "DiscoveryAspect": {
      "kind": "object",
      "properties": {
        "searchable": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "identifier": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "facet": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        },
        "measure": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "MeasureRole"
          }
        },
        "reportable": {
          "optional": true,
          "shape": {
            "kind": "boolean"
          }
        }
      }
    },
    "AspectOverlay": {
      "kind": "object",
      "properties": {
        "classification": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "ClassificationAspect"
          }
        },
        "access": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "AccessAspect"
          }
        },
        "lifecycle": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "LifecycleAspect"
          }
        },
        "discovery": {
          "optional": true,
          "shape": {
            "kind": "ref",
            "name": "DiscoveryAspect"
          }
        }
      }
    }
  }
}
""";
    private static readonly JsonDocument Model = JsonDocument.Parse(ModelJson);

    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Deserialize<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Validate(typeof(T).Name, document.RootElement);
        return JsonSerializer.Deserialize<T>(document.RootElement, Options)
            ?? throw new FormsWireException("invalid-null", typeof(T).Name);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static void Validate(string type, JsonElement value)
    {
        if (!Model.RootElement.GetProperty("declarations").TryGetProperty(type, out var shape))
            throw new FormsWireException("unknown-type", type);
        ValidateShape(shape, value, type);
    }

    private static void ValidateShape(JsonElement shape, JsonElement value, string type)
    {
        var kind = shape.GetProperty("kind").GetString();
        switch (kind)
        {
            case "ref":
                Validate(shape.GetProperty("name").GetString()!, value);
                return;
            case "string":
            case "openString":
                Require(value.ValueKind == JsonValueKind.String, "invalid-string", type);
                return;
            case "number":
                Require(value.ValueKind == JsonValueKind.Number, "invalid-number", type);
                return;
            case "boolean":
                Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "invalid-boolean", type);
                return;
            case "unknown":
                return;
            case "literal":
                Require(JsonElement.DeepEquals(value, shape.GetProperty("value")), "unknown-closed-value", type);
                return;
            case "enum":
                Require(shape.GetProperty("values").EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)), "unknown-closed-value", type);
                return;
            case "nullable":
                if (value.ValueKind != JsonValueKind.Null) ValidateShape(shape.GetProperty("value"), value, type);
                return;
            case "union":
                foreach (var member in shape.GetProperty("members").EnumerateArray())
                {
                    try { ValidateShape(member, value, type); return; } catch (FormsWireException) { }
                }
                throw new FormsWireException("unknown-closed-value", type);
            case "array":
                Require(value.ValueKind == JsonValueKind.Array, "invalid-array", type);
                foreach (var item in value.EnumerateArray()) ValidateShape(shape.GetProperty("element"), item, type);
                return;
            case "record":
                Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
                foreach (var property in value.EnumerateObject()) ValidateShape(shape.GetProperty("value"), property.Value, type);
                return;
            case "object":
                ValidateObject(shape, value, type);
                return;
            case "discriminatedUnion":
                ValidateDiscriminatedUnion(shape, value, type);
                return;
            case "exclude":
                ValidateShape(shape.GetProperty("source"), value, type);
                var excluded = shape.GetProperty("excluded");
                if (excluded.GetProperty("kind").GetString() == "literal")
                    Require(!JsonElement.DeepEquals(excluded.GetProperty("value"), value), "excluded-closed-value", type);
                else
                    Require(!excluded.GetProperty("values").EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)), "excluded-closed-value", type);
                return;
            case "constant":
                Require(JsonElement.DeepEquals(value, shape.GetProperty("value")), "constant-mismatch", type);
                return;
            default:
                throw new FormsWireException("unsupported-shape", type, kind);
        }
    }

    private static void ValidateObject(JsonElement shape, JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
        foreach (var property in shape.GetProperty("properties").EnumerateObject())
        {
            var present = value.TryGetProperty(property.Name, out var propertyValue);
            if (!present)
            {
                Require(property.Value.GetProperty("optional").GetBoolean(), "missing-required-property", type, property.Name);
                continue;
            }
            ValidateShape(property.Value.GetProperty("shape"), propertyValue, $"{type}.{property.Name}");
        }
    }

    private static void ValidateDiscriminatedUnion(JsonElement shape, JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
        var discriminatorName = shape.GetProperty("discriminator").GetString()!;
        Require(value.TryGetProperty(discriminatorName, out var discriminator) && discriminator.ValueKind == JsonValueKind.String, "unknown-discriminator", type);
        var discriminatorValue = discriminator.GetString()!;
        var variants = shape.GetProperty("variants");
        Require(variants.TryGetProperty(discriminatorValue, out var variant), "unknown-discriminator", type, discriminatorValue);
        var selected = variant.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var reserved = variants.EnumerateObject().SelectMany(candidate => candidate.Value.GetProperty("properties").EnumerateObject().Select(property => property.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (property.Name != discriminatorName && reserved.Contains(property.Name) && !selected.Contains(property.Name))
                throw new FormsWireException("variant-member-mismatch", type, property.Name);
        ValidateObject(variant, value, type);
    }


    private static void Require(bool condition, string code, string type, string? detail = null)
    {
        if (!condition) throw new FormsWireException(code, type, detail);
    }
}
