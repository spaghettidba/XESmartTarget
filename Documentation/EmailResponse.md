# EmailResponse

This Response type can be used to send an email each time an event is captured.

## Properties

* `string` **SMTPServer**: Address of the SMTP server

* `string` **Sender**: Sender email address

* `string` **To**: Address of the to recipient(s)

* `string` **Cc**: Address of the Cc recipient(s)

* `string` **Bcc**: Address of the Bcc recipient(s)

* `string` **Subject**: Subject of the mail message. Accepts placeholders in the text. Placeholders are in the form `{PropertyName}`, where PropertyName is one of the fields or actions available in the Event object. 
<BR>For instance, a valid Subject in a configuration file looks like this: `"An event of name {Name} occurred at {collection_time}"`

* `string` **Body**: Body of the mail message. The body can be static text or any property taken from the underlying event. See `Subject` for a description of how placeholders work.

* `bool` **HTMLFormat**: Send the email with HTML format. Default is `true`

* `string` **Attachment**: Data to attach to the email message. At this time, it can be any of the fields/actions of the underlying event. The data from the field/action is attached to the message as an ASCII stream. A single attachment is supported.

* `string` **AttachmentFileName**: File name to assign to the attachment.

* `string` **UserName**: User name to authenticate on the SMTP server. When blank, no authentication is performed.

* `string` **AttachmentFileName**: Password to authenticate on the SMTP server.