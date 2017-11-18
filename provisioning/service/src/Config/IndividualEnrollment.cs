﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace Microsoft.Azure.Devices.Provisioning.Service
{
    /// <summary>
    /// Representation of a single Device Provisioning Service enrollment and their accessors with a JSON serializer 
    ///     and deserializer.
    /// </summary>
    /// <remarks>
    /// This object is used to send and receive individualEnrollment information to and from the provisioning service.
    ///
    /// To create or update an Enrollment on the provisioning service you should fill this object and call the
    /// public API <see cref="ProvisioningServiceClient.CreateOrUpdateIndividualEnrollmentAsync(IndividualEnrollment)"/>.
    /// 
    /// The minimum information required by the provisioning service is the <code>RegistrationId</code> and the
    /// <code>Attestation</code>.
    ///
    /// A new device can be provisioned by two attestation mechanisms, Trust Platform Module (see <see cref=
    /// "TpmAttestation"/>) or X509 (see <see cref="X509Attestation"/>). The definition of each one you 
    /// should use depending on the physical authentication hardware that the device contains.
    ///
    /// The content of this class will be serialized in a JSON format and sent as a body of the rest API to the
    /// provisioning service. Or the content of this class can be filled by a JSON, received from the provisioning 
    /// service, as result of a individualEnrollment operation like create, update, or query. 
    /// </remarks>
    /// <example>
    /// When serialized, an individualEnrollment will look like the following example:
    /// <code>
    /// {
    ///     "registrationId":"validRegistrationId",
    ///     "deviceId":"ContosoDevice-123",
    ///     "attestation":{
    ///         "type":"tpm",
    ///         "tpm":{
    ///                "endorsementKey":"validEndorsementKey"
    ///         }
    ///     },
    ///     "iotHubHostName":"ContosoIoTHub.azure-devices.net",
    ///     "provisioningStatus":"enabled"
    /// }
    /// </code>
    /// 
    /// The following JSON is a sample of the individualEnrollment response, received from the provisioning service.
    /// <code>
    /// {
    ///     "registrationId":"validRegistrationId",
    ///     "deviceId":"ContosoDevice-123",
    ///     "attestation":{
    ///         "type":"tpm",
    ///         "tpm":{
    ///             "endorsementKey":"validEndorsementKey"
    ///         }
    ///     },
    ///     "iotHubHostName":"ContosoIoTHub.azure-devices.net",
    ///     "provisioningStatus":"enabled"
    ///     "createdDateTimeUtc": "2017-09-28T16:29:42.3447817Z",
    ///     "lastUpdatedDateTimeUtc": "2017-09-28T16:29:42.3447817Z",
    ///     "etag": "\"00000000-0000-0000-0000-00000000000\""
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="https://docs.microsoft.com/en-us/rest/api/iot-dps/deviceenrollment">Device Enrollment</seealso>
    public class IndividualEnrollment : IETagHolder
    {
        /// <summary>
        /// Creates a new instance of <code>IndividualEnrollment</code>.
        /// </summary>
        /// <remarks>
        /// This constructor creates an instance of the IndividualEnrollment object with the minimum set of 
        /// information required by the provisioning service. A valid individualEnrollment must contain the 
        /// registrationId, which uniquely identify this enrollment, and the attestation mechanism, which can 
        /// be TPM or X509.
        ///
        /// Other parameters can be added by calling the setters on this object.
        /// </remarks>
        /// <example>
        /// When serialized, an IndividualEnrollment will look like the following example:
        /// <code>
        /// {
        ///    "registrationId":"validRegistrationId",
        ///    "attestation":{
        ///        "type":"tpm",
        ///        "tpm":{
        ///            "endorsementKey":"validEndorsementKey"
        ///        }
        ///    }
        /// }
        /// </code>
        /// </example>
        /// <param name="registrationId"></param>
        /// <param name="attestation"></param>
        /// <exception cref="ArgumentException">if one of the provided parameters is not correct</exception>
        public IndividualEnrollment(string registrationId, Attestation attestation)
        {
            /* SRS_DEVICE_ENROLLMENT_21_001: [The constructor shall store the provided parameters.] */
            /* SRS_DEVICE_ENROLLMENT_21_002: [The constructor shall throws ArgumentException if one of the provided parameters is null.] */
            RegistrationId = registrationId ?? throw new ArgumentException("regitrationId cannot be null");
            Attestation = attestation ?? throw new ArgumentException("attestation cannot be null");
        }

        /// <summary>
        /// Creates a new instance of <code>IndividualEnrollment</code> using information in a JSON.
        /// </summary>
        /// <remarks>
        /// This constructor creates an instance of the enrollment filling the class with the information
        /// provided in the JSON. It is used by the SDK to parse enrollment responses from the provisioning service.
        /// </remarks>
        /// <example>
        /// The following JSON is a sample of the IndividualEnrollment response, received from the provisioning service.
        /// <code>
        /// {
        ///    "registrationId":"validRegistrationId",
        ///    "deviceId":"ContosoDevice-123",
        ///    "attestation":{
        ///        "type":"tpm",
        ///        "tpm":{
        ///            "endorsementKey":"validEndorsementKey"
        ///        }
        ///    },
        ///    "iotHubHostName":"ContosoIoTHub.azure-devices.net",
        ///    "provisioningStatus":"enabled"
        ///    "createdDateTimeUtc": "2017-09-28T16:29:42.3447817Z",
        ///    "lastUpdatedDateTimeUtc": "2017-09-28T16:29:42.3447817Z",
        ///    "etag": "\"00000000-0000-0000-0000-00000000000\""
        /// }
        /// </code>
        /// </example>
        /// <param name="registrationId">the <code>string</code> with a unique ide for the individualEnrollment. It cannot be <code>null</code> or empty.</param>
        /// <param name="attestation">the <see cref="AttestationMechanism"/> for the enrollment. It shall be `tpm` or `x509`.</param>
        /// <param name="deviceId">the <code>string</code> with the device name. This is optional and can be <code>null</code> or empty.</param>
        /// <param name="iotHubHostName">the <code>string</code> with the taget IoTHub name. This is optional and can be <code>null</code> or empty.</param>
        /// <param name="initialTwinState">the <see cref="TwinState"/> with the initial Twin condition. This is optional and can be <code>null</code>.</param>
        /// <param name="provisioningStatus">the <see cref="ProvisioningStatus"/> that determine the initial status of the device. This is optional and can be <code>null</code>.</param>
        /// <param name="createdDateTimeUtc">the <code>DateTime</code> with the date and time that the enrollment was created. This is optional and can be <code>null</code>.</param>
        /// <param name="lastUpdatedDateTimeUtc">the <code>DateTime</code> with the date and time that the enrollment was updated. This is optional and can be <code>null</code>.</param>
        /// <param name="eTag">the <code>string</code> with the eTag that identify the correct instance of the enrollment in the service. It cannot be <code>null</code> or empty.</param>
        /// <exception cref="ProvisioningServiceClientException">if the received JSON is invalid.</exception>
        [JsonConstructor]
        public IndividualEnrollment(
            string registrationId,
            AttestationMechanism attestation,
            string deviceId,
            string iotHubHostName,
            TwinState initialTwinState,
            ProvisioningStatus provisioningStatus,
            DateTime createdDateTimeUtc,
            DateTime lastUpdatedDateTimeUtc,
            string eTag)
        {
            if (string.IsNullOrWhiteSpace(eTag))
            {
                throw new ProvisioningServiceClientException("Service respond an individualEnrollment without eTag.");
            }
            try
            {
                RegistrationId = registrationId;
                DeviceId = deviceId;
                Attestation = attestation.GetAttestation();
                IotHubHostName = iotHubHostName;
                InitialTwinState = initialTwinState;
                ProvisioningStatus = provisioningStatus;
                CreatedDateTimeUtc = createdDateTimeUtc;
                LastUpdatedDateTimeUtc = lastUpdatedDateTimeUtc;
                ETag = eTag;
            }
            catch (ArgumentException e)
            {
                throw new ProvisioningServiceClientException(e);
            }
        }

        /// <summary>
        /// Convert this object in a pretty print format.
        /// </summary>
        /// <returns>The <code>string</code> with the content of this class in a pretty print format.</returns>
        public override string ToString()
        {
            string jsonPrettyPrint = Newtonsoft.Json.JsonConvert.SerializeObject(this, Formatting.Indented);
            return jsonPrettyPrint;
        }

        /// <summary>
        /// Registration ID
        /// </summary>
        [JsonProperty(PropertyName = "registrationId")]
        public string RegistrationId
        {
            get
            {
                return _RegistrationId;
            }

            /// <summary>
            /// Registration ID.
            /// </summary>
            /// <remarks>
            /// A valid registration Id shall be alphanumeric, lowercase, and may contain hyphens. Max characters 128.
            /// </remarks>
            /// <exception cref="ArgumentException">if the provided string do not fits the registration Id requirements</exception>
            private set
            {
                /* SRS_DEVICE_ENROLLMENT_21_003: [The setRegistrationId shall throws ArgumentException if the provided 
                                                    registrationId is null, empty, or invalid.] */
                ParserUtils.EnsureRegistrationId(value);
                _RegistrationId = value;
            }
        }
        private string _RegistrationId;

        /// <summary>
        /// Desired IoT Hub device ID (optional).
        /// </summary>
        [JsonProperty(PropertyName = "deviceId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string DeviceId
        {
            get
            {
                return _DeviceId;
            }
            set
            {
                /* SRS_DEVICE_ENROLLMENT_21_004: [The setDeviceId shall throws ArgumentException if the provided device 
                                                    is empty, or invalid.] */
                if (value == null)
                {
                    _DeviceId = null;
                }
                else
                {
                    ParserUtils.EnsureValidId(value);
                    _DeviceId = value;
                }
            }
        }
        private string _DeviceId;

        /// <summary>
        /// Current registration state.
        /// </summary>
        [JsonProperty(PropertyName = "registrationState", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public DeviceRegistrationState RegistrationState { get; private set; }

        /// <summary>
        /// Attestation Mechanism
        /// </summary>
        [JsonProperty(PropertyName = "attestation")]
        private AttestationMechanism _Attestation;
        [JsonIgnore]
        public Attestation Attestation
        {
            get
            {
                return _Attestation.GetAttestation();
            }
            set
            {
                _Attestation = new AttestationMechanism(value ?? throw new ArgumentException("Attestation cannot be null."));
            }
        }

        /// <summary>
        /// Desired IotHub to assign the device to
        /// </summary>
        [JsonProperty(PropertyName = "iotHubHostName", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string IotHubHostName { get; set; }

        /// <summary>
        /// Initial twin state.
        /// </summary>
        [JsonProperty(PropertyName = "initialTwinState", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TwinState InitialTwinState { get; set; }

        /// <summary>
        /// The provisioning status.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(PropertyName = "provisioningStatus", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ProvisioningStatus? ProvisioningStatus { get; set; }

        /// <summary>
        /// The DateTime this resource was created.
        /// </summary>
        [JsonProperty(PropertyName = "createdDateTimeUtc", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public DateTime? CreatedDateTimeUtc { get; private set; }

        /// <summary>
        /// The DateTime this resource was last updated.
        /// </summary>
        [JsonProperty(PropertyName = "lastUpdatedDateTimeUtc", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public DateTime? LastUpdatedDateTimeUtc { get; private set; }

        /// <summary>
        /// Enrollment's ETag
        /// </summary>
        [JsonProperty(PropertyName = "etag", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ETag { get; set; }
    }
}
