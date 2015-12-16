using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;


namespace FoliaParse {
  /* -------------------------------------------------------------------------------------
   * Name:  XmlConv
   * Goal:  Model class for xml to xml conversion
   * History:
   * 2/oct/2015 ERK Created
     ------------------------------------------------------------------------------------- */
  public abstract class XmlConv {
    // Variables valid for all implementations of XmlToXml
    protected ErrHandle errHandle;
    protected util.xmlTools oXmlTools;
    protected util.psdxTools oPsdxTools;
    protected String sCurrentSrcFile;     // Full path of current source file

    // ==========================================================
    // Abstract methods that  need to be implemented by derivates
    public abstract XmlNode oneSent(XmlNode ndxSentIn, String sSentId, String sArg, ref List<XmlNode> lWords);
    public abstract XmlNode stringToSent(String sSent);
    public abstract String getCurrentSrcFile();
    public abstract void setCurrentSrcFile(String sFile);
  }
}
