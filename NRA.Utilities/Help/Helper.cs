using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace NRA.Utilities.Help;

public class Helper
{
    public static void Skip()
    {
    }

    public static void Destructor(object @class)
    {
        var fields = @class.GetType().GetFields();
        {
            foreach (var field in fields)
                try
                {
                    field.SetValue(@class, GetTypeAndSetOfGetDefaultValueJson(field));
                }
                catch (Exception)
                {
                    // ignored.
                }
        }
    }

    // islands start;
    // island 1 start;
    public static int AttackAngleExtractMutation(int mutationStart, int mutationCounter, int mutationSpread,
        int mutationCount,
        int mutationStep)
    {
        mutationSpread = -mutationSpread + mutationStep;

        var deltaMutator = mutationSpread != 0 ? -mutationSpread / 4 : 0;
        {
            for (var i = 0; i < mutationCounter; i++)
                deltaMutator += mutationSpread != 0 ? mutationSpread / (2 * mutationCount) : 4;
        }

        return mutationStart + deltaMutator + 360 / 100;
    }

    public static int ConvertToRadians(double angle)
    {
        return (int)(Math.PI / 180 * angle);
    }
    // island 1 end;

    // island 2 start;
    public static void OnSend(IAsyncResult iAsyncResult)
    {
        try
        {
            ((Socket)iAsyncResult.AsyncState!).EndSend(iAsyncResult);
        }
        catch (Exception)
        {
            // ignored.
        }
    }

    public static string GetIpFromDomain(string domain)
    {
        return domain[..domain.IndexOf(':')];
    }

    public static int GetPortFromDomain(string domain)
    {
        return Convert.ToInt32(domain[(domain.IndexOf(':') + 1)..]);
    }

    public static IPEndPoint GetFullyEndPointByDomain(string domain)
    {
        return new IPEndPoint(IPAddress.Parse(GetIpFromDomain(domain)), GetPortFromDomain(domain));
    }

    public static string? GetIpBySocket(Socket socket)
    {
        return socket.RemoteEndPoint!.ToString()?
            [..socket.RemoteEndPoint.ToString()!.IndexOf(':')];
    }

    public static int? GetPortBySocket(Socket socket)
    {
        return Convert.ToInt32(socket.RemoteEndPoint!.ToString()?
            [(socket.RemoteEndPoint.ToString()!.IndexOf(':') + 1)..]);
    }
    // island 2 end;

    // island 3 start;
    public static byte[] XorWithKey(byte[] data, byte[] key)
    {
        var expandedKey = new byte[key.Length * (1 + 2)];
        {
            for (var i1 = 0; i1 < key.Length; i1++)
            {
                expandedKey[i1] = key[i1];

                expandedKey[i1 + key.Length] =
                    (byte)((byte)~key[i1] ^ (key[i1] >> (byte)~key[i1]));
                expandedKey[i1 + key.Length * 2] =
                    (byte)((byte)~key[i1] << key[i1] << (byte)~key[i1]);
            }
        }

        var result = new byte[data.Length];
        {
            for (var i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ expandedKey[i % expandedKey.Length]);
        }

        return result;
    }

    public static string XorWithKey(string input, string key)
    {
        var result = new StringBuilder();
        {
            for (var i = 0; i < input.Length; i++)
                result.Append((char)(input[i] ^ key[i % key.Length]));
        }

        return result.ToString();
    }

    public static string ComputeHmacSha256(string message, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    public static string ComputeMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        var sb = new StringBuilder();
        {
            foreach (var t in hashBytes)
                sb.Append(t.ToString("x2"));
        }

        return sb.ToString();
    }

    public static string Encrypt(string text, int shift)
    {
        var buffer = text.ToCharArray();
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var letter = buffer[i];
                letter = (char)(letter + shift);
                buffer[i] = letter;
            }
        }

        return new string(buffer);
    }

    public static string Decrypt(string text, int shift)
    {
        var buffer = text.ToCharArray();
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var letter = buffer[i];
                letter = (char)(letter - shift);
                buffer[i] = letter;
            }
        }

        return new string(buffer);
    }

    public static string GenerateRandomString(int length)
    {
        var result = new char[length];
        {
            for (var i = 0; i < result.Length; i++)
                result[i] = "ABDEFGHIJKLMNPQRSTUVWXYZabdefghijklmnpqrstuvwxyz123456789"[
                    new Random().Next("ABDEFGHIJKLMNPQRSTUVWXYZabdefghijklmnpqrstuvwxyz123456789".Length)];
        }

        return new string(result);
    }

    public static string ConvertStringToUnderscore(string input)
    {
        var charArray = input.ToCharArray();
        {
            for (var i = 0; i < charArray.Length; i++)
                if (charArray[i] == '_' && i < charArray.Length - 1)
                {
                    charArray[i] = char.ToUpper(charArray[i + 1]);
                    {
                        Array.Copy(charArray, i + 2, charArray, i + 1, charArray.Length - i - 2);
                        Array.Resize(ref charArray, charArray.Length - 1);
                    }
                }
                else if (i == 0)
                {
                    charArray[i] = char.ToLower(charArray[i]);
                }
        }

        return new string(charArray);
    }

    public static string ConvertStringToCamelCase(string str)
    {
        var result = new StringBuilder();
        {
            var capitalizeNext = false;
            {
                foreach (var currentChar in str)
                    if (currentChar == '_')
                    {
                        capitalizeNext = true;
                    }
                    else
                    {
                        if (capitalizeNext)
                        {
                            result.Append(char.ToUpper(currentChar));
                            capitalizeNext = false;
                        }
                        else
                        {
                            result.Append(char.ToLower(currentChar));
                        }
                    }
            }
        }

        return result.ToString();
    }

    public static byte[] ConvertStringToByteArray(string hex)
    {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }
    // island 3 end;

    // island 4 start;
    public static string GenerateToken(long id)
    {
        return GenerateRandomString(10) + id * 3 + GenerateRandomString(id < 100 ? 50 : 100);
    }

    public static string GenerateScIdToken(long id)
    {
        return "#SC-" + "PU" + id + "/" + GenerateRandomString(10) + id * 3 + ":";
    }
    // island 4 end;

    // island 5 start;
    /*public static bool GetIsAdequateString(string name)
    {
        return !name.Contains("tg", StringComparison.CurrentCultureIgnoreCase) &&
               !name.Contains("ddos", StringComparison.CurrentCultureIgnoreCase) &&
               !ProfanityAdministrator.ProfanityContainCheck(name, out _);
    }*/
    // island 5 end;

    // island 6 start;
    public static bool GetIsCorrectName(string nameForCheck)
    {
        return nameForCheck.Length is > 3 and <= 15;
    }

    public static int GetChangeNameCostByCount(int count)
    {
        return Math.Min(Math.Max(count, 0) * 30, 120);
    }
    // island 6 end;

    // island 7 start;
    public static int GenerateRandomIntForBetween(int min, int max)
    {
        return new Random().Next(min, max + 1);
    }

    public static bool GetChanceByPercentage(int percentage)
    {
        return new Random().Next(0, 100) <= percentage;
    }
    // island 7 end;

    // island 8 start;
    // island 8 end;

    // island 9 start;
    public static float Lerp(int a, int b, int t)
    {
        return a + (b - a) * t;
    }
    // island 9 end;

    // island 10 start;
    public static object GetTypeAndSetOfGetDefaultValueJson(FieldInfo field)
    {
        try
        {
            if (field.FieldType == typeof(string)) return "NULL";
            if (field.FieldType == typeof(int) || field.FieldType == typeof(long) ||
                field.FieldType == typeof(byte)) return -1;
            if (field.FieldType == typeof(bool)) return false;
            if (field.FieldType == typeof(double)) return (double)-1;
            if (field.FieldType == typeof(short)) return (short)-1;
            if (field.FieldType == typeof(float)) return -1f;
            if (field.FieldType == typeof(char)) return '\u0000';
            if (field.FieldType == typeof(Dictionary<object, object>)) return new Dictionary<object, object>();

            if (field.FieldType.IsArray)
            {
                var componentType = field.FieldType.GetElementType();
                {
                    if (componentType == typeof(int)) return Array.Empty<int>();
                    if (componentType == typeof(string)) return Array.Empty<string>();
                    if (componentType == typeof(char)) return Array.Empty<char>();
                    if (componentType == typeof(long)) return Array.Empty<long>();
                    if (componentType == typeof(byte)) return Array.Empty<byte>();
                    if (componentType == typeof(Dictionary<object, object>)) return new Dictionary<object, object>();
                }
            }
        }
        catch (Exception)
        {
            // ignored.
        }

        return null!;
    }

    public static List<int> SumRepeatedElements(IEnumerable<int> inputList)
    {
        var enumerable = inputList as int[] ?? inputList.ToArray();
        {
            if (enumerable.Length < 3) return enumerable.ToList();
        }

        var elementSumDictionary = new Dictionary<int, int>();

        foreach (var number in enumerable.ToList().Where(number => !elementSumDictionary.TryAdd(number, number)))
            elementSumDictionary[number] += number;
        return elementSumDictionary.Values.ToList();
    }

    /*public static string FixAutoimmuneFilePath(string appDomainBasePath)
    {
        return Regexes.NetPathRegexVestibular().Replace(appDomainBasePath.Replace("\\", "/"), string.Empty) + "/";
    }*/

    public static Dictionary<TKey, TValue> CloneDictionaryCloningValues<TKey, TValue>
        (Dictionary<TKey, TValue> original) where TValue : ICloneable where TKey : notnull
    {
        var ret = new Dictionary<TKey, TValue>(original.Count, original.Comparer);
        {
            foreach (var entry in original) ret.Add(entry.Key, (TValue)entry.Value.Clone());
        }

        return ret;
    }
    // island 10 end;
    // islands end;
}