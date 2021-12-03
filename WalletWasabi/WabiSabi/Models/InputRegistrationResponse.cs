using System;
using NBitcoin;
using WalletWasabi.WabiSabi.Crypto.CredentialRequesting;

namespace WalletWasabi.WabiSabi.Models
{
	public record InputRegistrationResponse(
		Guid AliceSecretId,
		CredentialsResponse AmountCredentials,
		CredentialsResponse VsizeCredentials
	);
}
