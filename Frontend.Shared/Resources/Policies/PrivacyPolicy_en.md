# Personal Data Processing Policy

**Revision dated 21 October 2026**

## 1. General provisions

1.1. This Policy defines the procedure for processing and protecting the personal data of users of the service "PLACEHOLDER: service name" (the "Service"), hosted on the Internet at "PLACEHOLDER: site address" (the "Site"), and constitutes the document determining the Operator's policy with respect to personal data processing.

1.2. **Personal data operator** — "PLACEHOLDER: full legal name of the legal entity / full name of the individual entrepreneur" (the "Operator"), OGRN "PLACEHOLDER: OGRN/OGRNIP", INN "PLACEHOLDER: INN", registered address: "PLACEHOLDER: registered address".

1.3. The Policy is drafted in accordance with Federal Law No. 152-FZ of 27.07.2006 "On Personal Data" (the "152-FZ"), the Labour Code of the Russian Federation (as regards processing data of students), other normative legal acts of the Russian Federation, and the recommendations of Roskomnadzor.

1.4. The Policy applies to information about users that the Operator obtains through the Site, and to personal data processed by the Operator in connection with the provision of the Service.

1.5. The terms "personal data", "processing of personal data", "automated processing", "dissemination", "operator" and "personal data subject" are used in the meanings set out in Article 3 of the 152-FZ.

1.6. This Policy cannot contradict the applicable legislation of the Russian Federation. In case of discrepancy, the legislation prevails.

## 2. Operator details and responsible person

2.1. **Person responsible for the organisation of personal data processing:** "PLACEHOLDER: full name, position, contact email".

2.2. **Contacts for personal data subjects' requests:** email "PLACEHOLDER: email for personal data requests", phone "PLACEHOLDER: phone", postal address: "PLACEHOLDER: postal address".

2.3. The Operator has notified Roskomnadzor of its intention to process personal data (notification dated "PLACEHOLDER: notification date", No. "PLACEHOLDER: notification reference number").

## 3. Categories of personal data subjects

3.1. The categories of personal data subjects whose data the Operator processes are:

- **students** — individuals who register on the Site to access learning materials, submission results, grades, attendance and the submission queue;
- **teachers and administrators** — individuals granted access to the administrative functions of the Service (creating subjects, tasks and groups, reviewing submissions, assigning grades, moderating the forum and surveys);
- **Site visitors** — individuals interacting with the Site without registering (viewing public result summaries);
- **contractors and event participants** — individuals who submit enquiries through the feedback forms on the Site.

3.2. The list of subject categories for each processing purpose is set out in Section 4 of this Policy.

## 4. Processing purposes, categories and list of data, retention periods and destruction procedure

4.1. The processing purposes are set out below. For each purpose the Policy specifies the categories and list of personal data, the categories of data subjects, the legal basis, the methods and periods of processing and storage, and the procedure for destroying the data.

### 4.1.1. User registration and identification

- **Purpose:** creating an account, identifying the user, assigning the user's role.
- **Categories and list of data:** surname, first name, email address, study group, password hash, registration date, preferred interface language.
- **Categories of subjects:** students, teachers and administrators.
- **Legal basis:** the subject's consent (Art. 6(1)(1) 152-FZ), performance of a contract (Art. 6(1)(2) 152-FZ).
- **Processing methods:** collection and recording with and without the use of automated tools; storage in the Operator's database; transfer over a protected communications channel.
- **Retention period:** until the purpose is achieved — for 3 (three) years from the user's last activity (last sign-in to the Service), unless a different period is set for specific data in this Policy.
- **Destruction:** on expiry of the retention period the account and the related profile are deleted by an automatic background procedure; where deletion is technically impossible, the data is anonymised within 30 days of the purpose being achieved.

### 4.1.2. Email address confirmation and account recovery

- **Purpose:** confirming that the email address belongs to the user, restoring access to the account, preventing fraudulent registrations.
- **Categories and list of data:** email address, confirmation code, password reset code, time the email was sent, requester's IP address.
- **Categories of subjects:** students, teachers and administrators.
- **Legal basis:** the subject's consent; performance of a contract.
- **Processing methods:** sending an email with a one-time code; storing the code as a temporary hash; logging the fact of sending.
- **Retention period:** confirmation and reset codes — no longer than 10 minutes from generation; send logs — 1 year.
- **Destruction:** codes expire automatically and are not reused; send logs are deleted when the retention period expires.

### 4.1.3. Provision of the Service functions (learning process)

- **Purpose:** signing up for submission of assignments, viewing submission results, grades, teacher comments, attendance and submission history.
- **Categories and list of data:** user identifier, study group, subject, fact and time of submission, points scored, submission status, grade, teacher comments, attendance marks, queue entries, materials published by the user (forum post texts, survey responses, profile images, profile and achievement links).
- **Categories of subjects:** students, teachers and administrators.
- **Legal basis:** performance of a contract (Art. 6(1)(2) 152-FZ); the subject's consent — for materials published at the user's discretion.
- **Processing methods:** recording, systematisation, accumulation, storage, updating (updating, modification), extraction, use; transfer within the Service between students and teachers to the extent required for the learning process.
- **Retention period:** learning activity records — for the period established by the internal regulations of the educational organisation "PLACEHOLDER: name of the educational organisation", but no longer than 5 (five) years from the last update; unless a different period is established — for 3 (three) years from the last activity.
- **Destruction:** on achievement of the purpose the data is deleted or anonymised within 30 days; on the subject's written request — within 10 working days from receipt of the request, unless legislation establishes a different period.

### 4.1.4. Informing users about Service events

- **Purpose:** notifying about queue status, review results, survey response publication and other events relating to the account.
- **Categories and list of data:** user identifier, email address, event identifier, notification text, event time.
- **Categories of subjects:** students, teachers and administrators.
- **Legal basis:** performance of a contract; the Operator's legitimate interests (Art. 6(1)(4) 152-FZ) — for technical system status notifications.
- **Processing methods:** creating and delivering notifications through the Service and by email.
- **Retention period:** notifications — 1 year from creation.
- **Destruction:** deleted when the retention period expires or when the account is deleted.

### 4.1.5. Security and prevention of abuse

- **Purpose:** protecting the Service and users' personal data from unauthorised access, detecting and preventing fraud, abuse and automated attacks.
- **Categories and list of data:** IP address, User-Agent, time and result of a sign-in attempt, failed attempt counter, account lockout flag, lockout duration, session identifier, refresh token hash.
- **Categories of subjects:** all Service users.
- **Legal basis:** the Operator's legitimate interests (Art. 6(1)(4) 152-FZ) — securing the Service; performance of a contract.
- **Processing methods:** automated processing, logging, access blocking.
- **Retention period:** security logs — 1 year; lockout records — until the lockout is lifted and for 1 year afterwards.
- **Destruction:** deleted when the retention period expires; in case of unlawful processing — within 10 working days from detection.

### 4.1.6. Audit of administrator actions

- **Purpose:** controlling administrator actions, investigating incidents, confirming the legitimacy of changes to academic data.
- **Categories and list of data:** administrator identifier and role, time, type and outcome of the administrative operation, identifier of the changed entity, anonymised values of the changed fields.
- **Categories of subjects:** teachers and administrators (as data subjects) and students (to the extent of identifiers to which audit records relate).
- **Legal basis:** the Operator's legitimate interests (Art. 6(1)(4) 152-FZ); requirements of Russian legislation.
- **Processing methods:** automated writing to the audit log; anonymisation of sensitive values.
- **Retention period:** no longer than 14 days from creation of the record with ring-finger storage; total archive size — no more than 128 files.
- **Destruction:** records are deleted as files rotate and when the stated period expires.

### 4.1.7. User enquiries

- **Purpose:** handling user enquiries, answering questions, accepting feedback.
- **Categories and list of data:** name provided by the user (if any), email address, enquiry text, attachments.
- **Categories of subjects:** Site visitors, Service users.
- **Legal basis:** the subject's consent; performance of a contract — for enquiries from Service users.
- **Processing methods:** collection using automated tools, reading, replying.
- **Retention period:** until the end of the correspondence and for 3 (three) years from the last enquiry.
- **Destruction:** deleted within 30 days of the purpose being achieved.

## 5. Procedure and conditions for processing

5.1. **Actions performed with personal data:** collection (obtaining from the subject, including through Site forms), recording, systematisation, accumulation, storage, updating (updating, modification), extraction, use, transfer (provision, access), anonymisation, blocking, deletion, destruction.

5.2. **Methods of processing:** both with the use of automated tools and without them.

5.3. **Sources of data:** data is obtained directly from the subject during registration and use of the Service, and as a result of the subject's actions in the Service (signing up for a submission, publishing posts, taking part in surveys).

5.4. **No obligation to provide data:** providing personal data is voluntary; however, refusing to provide data necessary for registration and for the Service to function makes the corresponding feature unavailable.

5.5. **Cross-border transfers:** the Operator does not carry out cross-border transfers of personal data, except where directly required by Russian legislation. Should such cases arise, the Operator informs the personal data subject in the manner and within the periods established by legislation.

5.6. **Localisation:** recording, systematisation, accumulation, storage and processing of the personal data of Russian citizens is carried out using databases located in the territory of the Russian Federation: "PLACEHOLDER: address of the database location (Russian Federation)". The Operator does not collect the personal data of Russian citizens for the first time using databases located outside the Russian Federation.

5.7. **Anonymisation:** where personal data is processed in audit logs, values of fields unrelated to the audit purposes are anonymised.

## 6. Transfer of personal data to third parties

6.1. The Operator does not transfer personal data to third parties, except where directly required by Russian legislation.

6.2. The Operator may engage personal data processors under data processing agreements (Art. 18.1 152-FZ). The list of engaged processors and a description of their functions:

- **hosting provider** — "PLACEHOLDER: name and address of the hosting provider": hosting and maintaining the servers on which the Service and the databases run;
- **email service provider** — "PLACEHOLDER: name and address of the email service provider": sending emails with confirmation and password reset codes and notifications;
- **monitoring and logging provider** — "PLACEHOLDER: name and address of the provider (if any)": collecting technical metrics and Service logs.

6.3. Transfer of personal data to authorised state bodies is carried out on the grounds and in the manner established by federal law, with mandatory notification to the personal data subject within 10 working days from the date of the request, unless otherwise provided by legislation.

## 7. Updating, destruction and responses to data subjects' requests

7.1. **Updating:** the Operator updates personal data when the information provided by the user changes, and when handling a personal data subject's request. Updating is carried out no later than 10 working days from receipt of the information or the request.

7.2. **Destruction:** the Operator destroys personal data when the processing purposes are achieved, when the need to achieve them is lost, when the subject withdraws consent, and on termination of the contract — within 30 days of the purpose being achieved, unless this Policy establishes a different period.

7.3. The Operator ensures destruction of personal data within 10 working days from detection of unlawful processing, unless otherwise established by federal law.

7.4. **Responses to requests:** the Operator provides the data subject with information about the processing of their personal data, and adopts and enforces decisions on the outcome of a request no later than 10 working days from receipt of the request, unless a different period is established by federal law. The period may be extended by no more than 5 working days upon sending the data subject a reasoned notification.

7.5. **Withdrawal of consent:** the data subject may withdraw consent to processing at any time by sending a written notice to the Operator. The Operator ceases processing and destroys the data within 10 working days from receipt of the withdrawal, unless otherwise provided by legislation or unless processing may continue on other grounds.

## 8. Information on implemented personal data protection requirements

8.1. The Operator takes the necessary legal, organisational and technical measures to protect personal data from unauthorised access, destruction, modification, blocking, copying, dissemination and other unlawful actions.

8.2. The implemented measures include, in particular:

- storing passwords as irreversible hashes (bcrypt / Argon2) — passwords are never stored or transmitted in clear text;
- transferring data over secure channels (HTTPS, TLS 1.2 and above) with a genuine server certificate;
- role-based access control (student, teacher, administrator, super-administrator) with server-side authorisation checks on every data access;
- short-lived access tokens, storage of refresh tokens only as hashes, their rotation and revocation on password change;
- rate limiting on authentication endpoints and automatic temporary account lockout after repeated failed sign-in attempts;
- cross-site request forgery protection (SameSite cookies, Content Security Policy);
- limits on the size and content validation of user-uploaded files, with images and video processed in an isolated environment with resource limits;
- regular database backups and verification of the ability to restore from them;
- logging of administrative actions with anonymisation of values.

8.3. The specific protection measures, their parameters and the internal control procedure are documented in separate internal documents of the Operator and, where necessary, in the agreements with the engaged processors.

## 9. Rights of the personal data subject

9.1. The personal data subject has the right to:

- receive information about the processing of their personal data, the purposes, legal bases, periods of processing and the list of data processed, as well as information about any cross-border transfers carried out;
- require the updating of their personal data if it is incomplete, outdated, inaccurate or unreliable;
- require the cessation of processing and the destruction of their personal data where processing is not carried out in accordance with legislation or where the purposes have been achieved;
- withdraw consent to the processing of personal data;
- appeal the Operator's actions (or inaction) to Roskomnadzor or apply to a court;
- request the exclusion of their personal data from publicly available result summaries — the Operator does not provide this facility, so such a request is considered on an individual basis within 10 working days.

9.2. To exercise their rights, the data subject contacts the Operator using the details in Section 2 of this Policy, or through the Service interface (the "My data" / "Profile settings" section — "PLACEHOLDER: exact path").

9.3. The Operator does not restrict the data subject's rights guaranteed by Russian legislation.

## 10. Amendments to the Policy

10.1. The Operator may amend this Policy. The current version is published on the Site at "PLACEHOLDER: address of the policy page".

10.2. For material amendments the Operator notifies users at the email address in their account and (or) by a notification in the Service interface no later than 10 days before the amendments take effect.

10.3. Policy revision dated 21 October 2026.