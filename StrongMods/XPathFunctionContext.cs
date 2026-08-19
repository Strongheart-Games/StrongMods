using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;
using HarmonyLib;

namespace StrongMods {
  /// <summary>Provides the <c>sm:</c> XPath extension namespace without exposing XPath variables.</summary>
  internal sealed class XPathInheritanceContext : XsltContext {
    public XPathInheritanceContext() : base(new NameTable()) {
    }

    public override bool Whitespace => false;
    public override int CompareDocument(string baseUri, string nextbaseUri) => 0;
    public override bool PreserveWhitespace(XPathNavigator node) => false;

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes) {
      return XPathInheritance.ResolveFunction(prefix, name, argTypes) ??
             throw new XPathException($"unknown XPath function \"{prefix}:{name}()\"");
    }

    public override IXsltContextVariable ResolveVariable(string prefix, string name) {
      throw new XPathException("XPath variables are available only inside foreach interpolation expressions");
    }
  }

  [HarmonyPatchCategory(XPathInheritance.Category)]
  [HarmonyPatch(typeof(XmlFile), nameof(XmlFile.GetXpathResultsInList))]
  public static class XmlFileGetXpathResultsInListPatch {
    public static bool Prefix(XmlFile __instance, string _xpath, List<XObject> _matchList, ref bool __result) {
      if (!XPathInheritance.HasFunctionMarker(_xpath)) {
        return true;
      }

      __result = XPathInheritance.TryGetMatches(__instance.XmlDoc, _xpath, _matchList);
      return false;
    }
  }
}
