using Microsoft.SqlServer.XEvent.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Responses
{
    public class TableAppenderResponse : Response
    {
        public TableAppenderResponse()
        {

        }

        public string TargetServer { get; set; }
        public string TargetDatabase { get; set; }
        public string TargetTable { get; set; }
        public int DelaySeconds { get; set; }
        public string OutputColumns { get; set; } // This is going to be used as the list of columns to output to the database table

        protected DataTable table = new DataTable();
        // This activates optimizations that allow to skip checks on the columns from events
        // Two possible actions when the more than on e event shows up in the queue: 
        // 1 - disable this flag, raise a warning and keep going without the optimizations
        // 2 - throw error.
        public bool IsSingleEvent = true;
        public bool RaiseErrorOnSingleEventViolation = false;

        public override void Process(PublishedEvent evt)
        {
            Enqueue(evt);
        }

        protected void Enqueue(PublishedEvent evt)
        {
            // TODO: Translate this comment
            // Fare attenzione a come si caricano gli eventi in questo punto
            // Se si tratta di un solo tipo di evento, le colonne saranno le stesse
            // Altrimenti sarà difficile attribuire le stesse colonne ad eventi diversi
            // Bisognerà anche aggiungere una lista delle colonne da salvare e matcharle
            // Con le colonne della tabella nel db. Posso anche aggiungere delle colonne fittizie
            // come il nome dell'evento, l'ora in cui si è registrato e cose così
            // Meglio usare diverse DataTable per diversi eventi?
            // Come gestisco la concorrenza dei thread? Meglio accodare in modo seriale
            // Se voglio mandare eventi diversi su tabelle diverse, farò in modo di aggiungere
            // Response di tipo diverso per eventi diversi
        }

        protected void Upload()
        {

        }


        private DataTable ReadEvent(PublishedEvent evt)
        {
            DataTable dt = new DataTable("events");

            //
            // Add computed columns
            //
            DataColumn cl_dt = new DataColumn("collection_time", typeof(DateTime))
            {
                DefaultValue = DateTime.Now
            };
            cl_dt.ExtendedProperties.Add("auto_column", true);
            dt.Columns.Add(cl_dt);


            //
            // Add Name column
            //
            dt.Columns.Add("Name", typeof(String));
            dt.Columns["Name"].ExtendedProperties.Add("auto_column", true);

            //
            // Read event data
            //
            foreach (PublishedEventField fld in evt.Fields)
            {
                DataColumn dc = null;
                if (fld.Type.IsSerializable)
                {
                    dc = dt.Columns.Add(fld.Name, fld.Type);
                }
                else
                {
                    dc = dt.Columns.Add(fld.Name, typeof(String));
                }
                dc.ExtendedProperties.Add("subtype", "field");
            }

            foreach (PublishedAction act in evt.Actions)
            {
                DataColumn dc = dt.Columns.Add(act.Name, act.Type);
                dc.ExtendedProperties.Add("subtype", "action");
            }

            DataRow row = dt.NewRow();
            row.SetField("Name", evt.Name);

            foreach (PublishedEventField fld in evt.Fields)
            {
                row.SetField(fld.Name, fld.Value);
            }

            foreach (PublishedAction act in evt.Actions)
            {
                row.SetField(act.Name, act.Value);
            }

            dt.Rows.Add(row);

            return dt;
        }

    }
}
